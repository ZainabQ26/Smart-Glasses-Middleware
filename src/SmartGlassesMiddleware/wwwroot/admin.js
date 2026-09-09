/**
 * Returns a translated UI string for the current admin language.
 * Falls back to the English string if the key is missing in the current language.
 * Reads from window.adminUiStrings which is populated by the inline script in admin.html.
 */
function adminT(key, fallback) {
    var strings = window.adminUiStrings;
    return (strings && strings[key] !== undefined) ? strings[key] : (fallback || key);
}

async function loadPrompts(reset = false) {
    if (state.prompts.loading) return;
    state.prompts.loading = true;
    showLoading('prompts-list');
    try {
        hideLoading('prompts-list');
        const list = document.getElementById('prompts-list');
        if (list) list.innerHTML = '';
    } finally {
        state.prompts.loading = false;
        hideLoading('prompts-list');
    }
}

// Stub implementations for features not present in the current admin UI.
// These functions are referenced by shared code paths (tab handlers, form
// handlers, infinite-scroll setup, etc.) that were written to support a
// richer admin UI.  The stubs prevent ReferenceErrors from halting script
// execution — without them line 2986 throws before init() is ever called,
// which means the conversation Send button and all file-upload listeners
// inside init() are never attached.

function createNewPrompt() {
    // Prompt creation UI not present in this view; no-op stub.
}

function cancelEditGroup() {
    // Groups UI not present in this view; no-op stub.
    const form = document.getElementById('group-form');
    if (form) form.reset();
    const idField = document.getElementById('group-id');
    if (idField) idField.value = '';
    isEditingGroup = false;
}

function closeManageGroupModal() {
    // Manage-group modal not present in this view; no-op stub.
    const modal = document.getElementById('manage-group-modal');
    if (modal) modal.classList.remove('active');
}

function closeReminderFilePreviewModal() {
    // Reminder file-preview modal not present in this view; no-op stub.
    const modal = document.getElementById('reminder-file-preview-modal');
    if (modal) modal.style.display = 'none';
}

async function loadReminders() {
    // Reminders UI not present in this view; no-op stub.
}

async function loadGroups() {
    // Groups UI not present in this view; no-op stub.
}

function updateGroupFilesList() {
    // Groups file list UI not present in this view; no-op stub.
    const list = document.getElementById('group-files-list');
    if (!list) return;
    // Render whatever is in groupSelectedFiles if the element exists.
    list.innerHTML = (window.groupSelectedFiles || []).map(f =>
        `<div class="file-item">${f.name || (f.file && f.file.name) || 'File'}</div>`
    ).join('');
}

function updateReminderFilesList() {
    // Reminders file list UI not present in this view; no-op stub.
    const list = document.getElementById('reminder-files-list');
    if (!list) return;
    list.innerHTML = (window.reminderSelectedFiles || []).map(f =>
        `<div class="file-item">${f.name || (f.file && f.file.name) || 'File'}</div>`
    ).join('');
}
const API_BASE = '/api/admin';
const ADMIN_DEBUG = !!window.__ADMIN_DEBUG__;
const themeStorageKey = 'uiTheme';
const ADMIN_SESSION_KEEPALIVE_INTERVAL_MS = 2.5 * 60 * 1000;
const MAX_UPLOAD_STATUS_TRANSIENTS_BEFORE_DEFER = 5;
const MIN_UPLOAD_STATUS_DEFER_RETRY_MS = 60000;
const MAX_UPLOAD_STATUS_DEFER_RETRY_MS = 300000;
// After this many deferred retries the upload is presumed permanently stuck; the user is told to re-upload.
const MAX_POLL_DEFERRED_BEFORE_ABANDON = 8;
const ADMIN_WAKE_RETRY_MS = 15000;
let adminSessionKeepAliveTimer = null;
let lastAdminWakeAttempt = 0;

// ── HTTP Polling (replaces SignalR hubs) ────────────────────────────────────

// Health polling — drives the connectivity banner.
const HEALTH_POLL_INTERVAL_MS = 15000;
const MAX_CONSECUTIVE_HEALTH_FAILURES = 3;
let healthPollTimer = null;
let consecutiveHealthFailures = 0;
let healthPollingActive = false;

// Resource polling — periodically invalidates cached files so fresh data
// is picked up the next time the Files tab is loaded.
const RESOURCE_POLL_INTERVAL_MS = 30000;
let resourcePollTimer = null;

// Groq live-transcription polling — only active when the Groq Live tab is open.
const GROQ_LIVE_POLL_INTERVAL_MS = 3000;
let groqLivePollTimer = null;
let groqLiveLastCount = 0;
let groqLiveEntries = [];

function triggerAdminWake() {
    const now = Date.now();
    if (now - lastAdminWakeAttempt < ADMIN_WAKE_RETRY_MS) {
        return;
    }

    lastAdminWakeAttempt = now;
    fetch('/wake', { method: 'POST' }).catch(() => {
        // Wake signal is best-effort; readiness polling continues.
    });
}

async function sendAdminSessionKeepAlivePing() {
    try {
        await fetch('/health', {
            method: 'GET',
            cache: 'no-store'
        });
    } catch (_) {
        // Best-effort ping; retry on next interval.
    }
}

function stopAdminSessionKeepAlive() {
    if (adminSessionKeepAliveTimer) {
        clearInterval(adminSessionKeepAliveTimer);
        adminSessionKeepAliveTimer = null;
    }
}

function startAdminSessionKeepAlive() {
    if (adminSessionKeepAliveTimer) {
        return;
    }

    sendAdminSessionKeepAlivePing();
    adminSessionKeepAliveTimer = setInterval(sendAdminSessionKeepAlivePing, ADMIN_SESSION_KEEPALIVE_INTERVAL_MS);
}

// ── Health polling — banner ─────────────────────────────────────────────────

async function runHealthPoll() {
    try {
        const res = await fetch('/health', { cache: 'no-store' });
        if (res.ok) {
            if (consecutiveHealthFailures >= MAX_CONSECUTIVE_HEALTH_FAILURES) {
                setSignalRIndicatorState(true);
            }
            consecutiveHealthFailures = 0;
        } else {
            consecutiveHealthFailures++;
            if (consecutiveHealthFailures >= MAX_CONSECUTIVE_HEALTH_FAILURES) {
                setSignalRIndicatorState(false);
            }
        }
    } catch (_) {
        consecutiveHealthFailures++;
        if (consecutiveHealthFailures >= MAX_CONSECUTIVE_HEALTH_FAILURES) {
            setSignalRIndicatorState(false);
        }
    }
}

function startHealthPolling() {
    if (healthPollTimer) return;
    healthPollingActive = true;
    runHealthPoll();
    healthPollTimer = setInterval(runHealthPoll, HEALTH_POLL_INTERVAL_MS);
}

function stopHealthPolling() {
    healthPollingActive = false;
    if (healthPollTimer) {
        clearInterval(healthPollTimer);
        healthPollTimer = null;
    }
}

// ── Resource polling — invalidate caches periodically ──────────────────────

function runResourcePoll() {
    // Quietly invalidate caches so the next tab-switch or manual refresh picks
    // up fresh data without showing any toast notification.
    apiCache.invalidate('files');
    apiCache.invalidate('batch');
}

function startResourcePolling() {
    if (resourcePollTimer) return;
    resourcePollTimer = setInterval(runResourcePoll, RESOURCE_POLL_INTERVAL_MS);
}

function stopResourcePolling() {
    if (resourcePollTimer) {
        clearInterval(resourcePollTimer);
        resourcePollTimer = null;
    }
}

// ── Groq live-transcription polling ────────────────────────────────────────

function renderGroqLiveEntry(entry) {
    const isCommand = entry.type === 'command';
    const time = entry.timestampUtc
        ? new Date(entry.timestampUtc).toLocaleTimeString()
        : '';
    const model = (entry.model || '').trim();

    const el = document.createElement('div');
    el.style.cssText = 'display:flex;align-items:flex-start;gap:10px;padding:8px 10px;background:#fff;border:1px solid #e0e0e0;border-radius:6px;font-size:14px;';

    // Badge — type is always "command" or "wakeword" (server-controlled), built safely
    const badge = document.createElement('span');
    badge.textContent = entry.type || 'unknown';
    badge.style.cssText = 'font-size:11px;padding:2px 7px;border-radius:10px;color:#fff;font-weight:600;flex-shrink:0;background:' +
        (isCommand ? '#0b63a6' : '#7f8c8d');

    // Text — user-provided transcription content, set via textContent to prevent XSS
    const text = document.createElement('span');
    text.style.cssText = 'flex:1;word-break:break-word;';
    text.textContent = entry.text || '(empty)';

    const meta = document.createElement('div');
    meta.style.cssText = 'display:flex;flex-direction:column;align-items:flex-end;gap:2px;flex-shrink:0;';

    const modelEl = document.createElement('span');
    modelEl.style.cssText = 'font-size:11px;color:#4b5563;white-space:nowrap;';
    modelEl.textContent = model ? model : 'model: unknown';

    // Timestamp
    const ts = document.createElement('span');
    ts.style.cssText = 'font-size:11px;color:#aaa;flex-shrink:0;white-space:nowrap;';
    ts.textContent = time;

    meta.appendChild(modelEl);
    meta.appendChild(ts);

    el.appendChild(badge);
    el.appendChild(text);
    el.appendChild(meta);
    return el;
}

async function runGroqLivePoll() {
    try {
        const res = await fetch(`${API_BASE}/groq-transcriptions?maxCount=200`, { cache: 'no-store' });
        if (!res.ok) return;
        const data = await res.json();
        const entries = data.entries || [];
        if (entries.length === groqLiveLastCount) return; // nothing new

        groqLiveEntries = entries;
        groqLiveLastCount = entries.length;

        const list = document.getElementById('groq-live-list');
        const empty = document.getElementById('groq-live-empty');
        if (!list) return;

        if (entries.length === 0) {
            if (empty) empty.style.display = '';
            // Remove any rendered rows but keep the empty placeholder
            [...list.children].forEach(c => { if (c !== empty) c.remove(); });
            return;
        }

        if (empty) empty.style.display = 'none';

        // Rebuild from scratch (simple — list is bounded to 200 entries)
        [...list.children].forEach(c => { if (c !== empty) c.remove(); });
        const frag = document.createDocumentFragment();
        // Show newest-first
        for (let i = entries.length - 1; i >= 0; i--) {
            frag.appendChild(renderGroqLiveEntry(entries[i]));
        }
        list.insertBefore(frag, empty || null);
    } catch (err) {
        console.warn('[GroqLive] poll error:', err);
    }
}

function startGroqLivePolling() {
    if (groqLivePollTimer) return;
    // Update status indicator
    const status = document.getElementById('groq-live-status');
    if (status) {
        status.textContent = 'Live';
        status.style.background = '#27ae60';
        status.style.color = '#fff';
    }
    runGroqLivePoll();
    groqLivePollTimer = setInterval(runGroqLivePoll, GROQ_LIVE_POLL_INTERVAL_MS);
}

function stopGroqLivePolling() {
    if (groqLivePollTimer) {
        clearInterval(groqLivePollTimer);
        groqLivePollTimer = null;
    }
    const status = document.getElementById('groq-live-status');
    if (status) {
        status.textContent = 'Inactive';
        status.style.background = '#ecf0f1';
        status.style.color = '#7f8c8d';
    }
}

// In-memory cache for API responses (no browser storage)
const apiCache = {
    data: new Map(),
    ttl: 30000, // 30 seconds TTL
    
    get(key) {
        const item = this.data.get(key);
        if (!item) return null;
        
        if (Date.now() > item.expiry) {
            this.data.delete(key);
            return null;
        }
        
        return item.value;
    },
    
    set(key, value, customTtl = null) {
        this.data.set(key, {
            value,
            expiry: Date.now() + (customTtl || this.ttl)
        });
    },
    
    invalidate(pattern) {
        if (pattern) {
            // Invalidate keys matching pattern
            for (const key of this.data.keys()) {
                if (key.includes(pattern)) {
                    this.data.delete(key);
                }
            }
        } else {
            // Clear all
            this.data.clear();
        }
    },
    
    clear() {
        this.data.clear();
    }
};

// Helper function to get property with case-insensitive fallback
function getProp(obj, propName) {
    if (!obj) return undefined;
    // Try lowercase first
    if (obj[propName] !== undefined) return obj[propName];
    // Try uppercase first letter
    const upperProp = propName.charAt(0).toUpperCase() + propName.slice(1);
    if (obj[upperProp] !== undefined) return obj[upperProp];
    // Try all uppercase
    if (obj[propName.toUpperCase()] !== undefined) return obj[propName.toUpperCase()];
    return undefined;
}

// Enhanced fetch with retry logic, caching, and deduplication
const pendingRequests = new Map();

async function fetchWithRetry(url, options = {}, retries = 1) {
    const method = options.method || 'GET';
    const cacheKey = `${method}:${url}`;
    
    // For GET requests, check cache first
    if (method === 'GET') {
        const cached = apiCache.get(cacheKey);
        if (cached) {
            console.log(`[API] Cache hit: ${url}`);
            return new Response(JSON.stringify(cached), {
                status: 200,
                headers: { 'Content-Type': 'application/json' }
            });
        }
        
        // Check if request is already pending (deduplication)
        if (pendingRequests.has(cacheKey)) {
            console.log(`[API] Deduplicating request: ${url}`);
            // Return a fresh clone of the underlying response so each caller
            // can consume the body independently (avoids "Body is disturbed or locked").
            const underlying = pendingRequests.get(cacheKey);
            return underlying.then(res => res.clone());
        }
    }
    
    // Add cache busting parameter only for non-cached requests
    const cacheBuster = `_=${Date.now()}`;
    const separator = url.includes('?') ? '&' : '?';
    const bustUrl = method === 'GET' ? `${url}${separator}${cacheBuster}` : url;
    
    const fetchOptions = {
        ...options,
        // Do not override Content-Type when the body is FormData; the browser sets the
        // correct multipart/form-data boundary automatically when the header is absent.
        ...(options.body instanceof FormData
            ? {}
            : {
                headers: {
                    ...options.headers,
                    'Content-Type': options.headers?.['Content-Type'] || 'application/json'
                }
            })
    };
    
    // Create the fetch promise
    const fetchPromise = (async () => {
        for (let i = 0; i < retries; i++) {
            try {
                console.log(`[API] Request: ${method} ${url}${i > 0 ? ` (retry ${i})` : ''}`);

                const response = await fetch(bustUrl, fetchOptions);
                console.log(`[API] Response: ${response.status} ${response.statusText} for ${url}`);

                if (response.status === 503) {
                    noteServiceOutage(`HTTP 503 from ${url}`);
                } else if (response.ok) {
                    clearServiceOutage(`HTTP ${response.status} from ${url}`);
                }

                // Clone response for caching
                if (response.ok && method === 'GET') {
                    const clone = response.clone();
                    clone.json().then(data => {
                        // Never cache degraded fallback payloads; they represent
                        // temporary backend pressure and would otherwise create stale loops.
                        if (!isDegradedPagedResult(data)) {
                            apiCache.set(cacheKey, data);
                        }
                    }).catch(() => {
                        // Ignore cache errors
                    });
                }

                if (!response.ok && response.status >= 500 && response.status !== 504 && i < retries - 1) {
                    console.warn(`[API] Server error, retrying in ${(i + 1) * 1000}ms...`);
                    await new Promise(resolve => setTimeout(resolve, (i + 1) * 1000));
                    continue;
                }

                return response;
            } catch (error) {
                console.error(`[API] Network error on attempt ${i + 1}:`, error);
                if (isServiceUnavailableError(error)) {
                    noteServiceOutage(`network failure for ${url}`);
                }
                if (i < retries - 1) {
                    await new Promise(resolve => setTimeout(resolve, (i + 1) * 1000));
                    continue;
                }
                throw error;
            }
        }

        throw new Error(`Failed to fetch ${url} after ${retries} attempts`);
    })();
    
    // Store pending request for deduplication
    if (method === 'GET') {
        pendingRequests.set(cacheKey, fetchPromise);
        fetchPromise.finally(() => {
            pendingRequests.delete(cacheKey);
        });
    }
    
    return fetchPromise;
}

// Helper to normalize attached file data into a consistent shape
function normalizeFileMeta(file) {
    if (!file) return null;
    if (typeof file === 'string') return { name: file };

    const name = file.name
        || file.fileName
        || file.FileName
        || file.OriginalFileName
        || file.originalFileName
        || 'Document';
    const id = file.id
        || file.documentId
        || file.DocumentId
        || file.Id
        || file.DocumentMetadataId
        || file.documentMetadataId
        || '';

    return { name, id };
}

function normalizeFileList(files) {
    if (!Array.isArray(files) || files.length === 0) return [];
    return files.map(normalizeFileMeta).filter(Boolean);
}

// Helper to render markdown as HTML
function renderMarkdown(text) {
    if (typeof marked !== 'undefined' && marked.parse) {
        try {
            // Configure marked for security
            marked.setOptions({
                breaks: true,
                gfm: true,
                headerIds: false,
                mangle: false
            });
            const rawHtml = marked.parse(text);
            // Sanitize the HTML to prevent XSS attacks
            if (typeof DOMPurify !== 'undefined') {
                return DOMPurify.sanitize(rawHtml);
            }
            return rawHtml;
        } catch (e) {
            console.warn('Failed to parse markdown:', e);
            return `<pre style="text-align: left; max-height: 400px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
        }
    }
    // Fallback if marked is not loaded
    return `<pre style="text-align: left; max-height: 400px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
}

// Helper to safely encode attached files array into a URL-safe JSON string
function encodeFilesData(msgFiles) {
    if (!Array.isArray(msgFiles) || msgFiles.length === 0) return '';
    try {
        return encodeURIComponent(JSON.stringify(normalizeFileList(msgFiles)));
    } catch (e) {
        console.warn('Failed to encode files data', e);
        return '';
    }
}

// Pagination state
const state = {
    files: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    prompts: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    groups: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    reminders: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    allGroups: [] // Cache of all groups for quick-add
};

var conversationHubConnection = null;
var resourceHubConnection = null;
var taskHubConnection = null;
let activeConversationId = null;
const HUB_RECONNECT_DELAYS_MS = [10000, 20000, 40000, 80000, 120000];
let conversationHubConnectInProgress = false;
let resourceHubConnectInProgress = false;
let taskHubConnectInProgress = false;
let conversationHubReconnectTimer = null;
let conversationHubReconnectAttempt = 0;
let resourceHubReconnectTimer = null;
let resourceHubReconnectAttempt = 0;
let taskHubReconnectTimer = null;
let taskHubReconnectAttempt = 0;

// Track individual hub connection states; banner shows if either hub is disconnected
// Default to `true` so the banner is hidden until a hub explicitly reports disconnected.
let resourceHubConnected = true;
let conversationHubConnected = true;

function updateSignalRBanner() {
    const warning = document.getElementById('signalr-warning');
    if (!warning) return;
    try {
        if (resourceHubConnected && conversationHubConnected) {
            warning.style.setProperty('display', 'none', 'important');
            warning.style.setProperty('visibility', 'hidden', 'important');
            warning.style.setProperty('opacity', '0', 'important');
        } else {
            warning.textContent = 'Server unreachable. Retrying...';
            warning.style.setProperty('display', 'block', 'important');
            warning.style.setProperty('visibility', 'visible', 'important');
            warning.style.setProperty('opacity', '1', 'important');
            warning.style.setProperty('position', 'fixed', 'important');
            warning.style.setProperty('top', '0', 'important');
            warning.style.setProperty('left', '0', 'important');
            warning.style.setProperty('width', '100%', 'important');
            warning.style.setProperty('z-index', '20000', 'important');
        }
    } catch (e) {
        console.warn('[Admin] Unable to toggle warning bar', e);
    }
}

function setResourceHubState(connected) {
    resourceHubConnected = !!connected;
    updateSignalRBanner();
}

function setConversationHubState(connected) {
    conversationHubConnected = !!connected;
    updateSignalRBanner();
}

// Backwards-compatible wrapper used by tests and any callers that expect the old API
function setSignalRIndicatorState(connected) {
    const warning = document.getElementById('signalr-warning');
    try {
        if (!warning) return;
        if (connected) {
            resourceHubConnected = true;
            conversationHubConnected = true;
            warning.style.display = 'none';
            warning.style.visibility = 'hidden';
            warning.style.opacity = '0';
        } else {
            resourceHubConnected = false;
            conversationHubConnected = false;
            warning.textContent = 'Server unreachable. Retrying...';
            warning.style.display = 'block';
            warning.style.visibility = 'visible';
            warning.style.opacity = '1';
            warning.style.position = 'fixed';
            warning.style.top = '0';
            warning.style.left = '0';
            warning.style.width = '100%';
            warning.style.zIndex = '20000';
        }
    } catch (e) {
        console.warn('[Admin] Unable to toggle warning bar (compat wrapper)', e);
    }
}

// Track active requests for progress display
const activeRequests = new Map();
const requestActivities = new Map();

function normalizeUploadFileName(value) {
    return String(value || '')
        .trim()
        .replace(/\s+/g, ' ')
        .toLowerCase();
}

function parseUploadNameFromText(text) {
    const raw = String(text || '').trim();
    if (!raw) {
        return '';
    }

    const uploadingMatch = raw.match(/^uploading:\s*(.+)$/i);
    if (uploadingMatch?.[1]) {
        return uploadingMatch[1].trim();
    }

    const completedMatch = raw.match(/^file\s+(.+?)\s+(?:uploaded|upload failed|failed)/i);
    if (completedMatch?.[1]) {
        return completedMatch[1].trim();
    }

    return raw;
}

function getTaskHubUploadName(request) {
    const directName = getProp(request, 'fileName')
        || getProp(request, 'label')
        || getProp(request, 'name');
    if (directName) {
        return String(directName).trim();
    }

    return parseUploadNameFromText(getProp(request, 'details') || getProp(request, 'description') || '');
}

function countPendingPromptFiles() {
    if (!Array.isArray(promptSelectedFiles)) {
        return 0;
    }

    return promptSelectedFiles.filter(fileObj => fileObj && fileObj.pending).length;
}

function findPendingPromptFileForTaskHub(requestId, request) {
    if (!Array.isArray(promptSelectedFiles) || promptSelectedFiles.length === 0) {
        return null;
    }

    const normalizedRequestId = String(requestId || '').trim();
    const normalizedUploadName = normalizeUploadFileName(getTaskHubUploadName(request));
    let firstPendingFile = null;

    for (const fileObj of promptSelectedFiles) {
        if (!fileObj || !fileObj.pending) {
            continue;
        }

        if (!firstPendingFile) {
            firstPendingFile = fileObj;
        }

        const fileRequestId = String(fileObj.requestId || '').trim();
        if (normalizedRequestId && fileRequestId && fileRequestId === normalizedRequestId) {
            return fileObj;
        }

        if (!fileRequestId && normalizedUploadName) {
            const pendingName = normalizeUploadFileName(fileObj.file?.name || fileObj.name || '');
            if (pendingName && pendingName === normalizedUploadName) {
                return fileObj;
            }
        }
    }

    // If exactly one upload is pending, bind task updates to it even when metadata is partial.
    if (normalizedRequestId && countPendingPromptFiles() === 1) {
        return firstPendingFile;
    }

    return null;
}

function applyTaskHubUploadStatus(request) {
    if (!Array.isArray(promptSelectedFiles) || promptSelectedFiles.length === 0) {
        return;
    }

    const requestId = String(getProp(request, 'requestId') || '').trim();
    const statusRaw = String(getProp(request, 'status') || '').trim();
    if (!statusRaw) {
        return;
    }

    const requestType = String(getProp(request, 'requestType') || '').trim().toLowerCase();
    const isUploadType = requestType === 'fileupload' || requestType === 'documentupload';
    const hasExactRequestMatch = !!(requestId && promptSelectedFiles.some(fileObj => {
        if (!fileObj || !fileObj.pending) {
            return false;
        }
        const fileRequestId = String(fileObj.requestId || '').trim();
        return fileRequestId && fileRequestId === requestId;
    }));

    if (!isUploadType && !hasExactRequestMatch) {
        return;
    }

    const status = statusRaw.toLowerCase();
    const details = String(getProp(request, 'details') || getProp(request, 'description') || '').trim();
    const progressRaw = getProp(request, 'progress');
    const progress = Number.isFinite(Number(progressRaw)) ? Math.max(0, Math.min(100, Number(progressRaw))) : null;

    const isSuccess = status === 'completed' || status === 'complete' || status === 'success' || status === 'done';
    const isFailure = status === 'failed' || status === 'error' || status === 'cancelled' || status === 'canceled';

    const matchedFile = findPendingPromptFileForTaskHub(requestId, request);
    if (!matchedFile) {
        return;
    }

    if (requestId && !matchedFile.requestId) {
        matchedFile.requestId = requestId;
    }

    if (isFailure) {
        promptSelectedFiles = promptSelectedFiles.filter(fileObj => fileObj !== matchedFile);
        syncPromptSelectedFilesState();
        showMessage(details || 'File upload failed on the server.', 'error');
        return;
    }

    if (progress !== null) {
        matchedFile.uploadProgress = progress;
    }

    if (isSuccess) {
        matchedFile.pending = false;
        matchedFile.statusText = '';
        matchedFile.uploadProgress = 100;
        matchedFile._availabilityMonitorStarted = false;
        syncPromptSelectedFilesState();

        loadFiles(true).catch(error => {
            console.warn('Failed to refresh files after TaskHub completion', error);
        });
        return;
    }

    matchedFile.statusText = details || (status === 'uploading' ? 'Uploading to server...' : 'Finishing upload...');
    syncPromptSelectedFilesState();
}

function applyTaskHubUploadActivity(activity) {
    if (!Array.isArray(promptSelectedFiles) || promptSelectedFiles.length === 0) {
        return;
    }

    const requestId = String(getProp(activity, 'requestId') || '').trim();
    if (!requestId) {
        return;
    }

    const matchedFile = findPendingPromptFileForTaskHub(requestId, activity);
    if (!matchedFile) {
        return;
    }

    if (!matchedFile.requestId) {
        matchedFile.requestId = requestId;
    }

    const progressRaw = getProp(activity, 'progress');
    const progress = Number.isFinite(Number(progressRaw)) ? Math.max(0, Math.min(100, Number(progressRaw))) : null;
    const activityText = String(getProp(activity, 'activity') || getProp(activity, 'details') || '').trim();

    if (progress !== null) {
        matchedFile.uploadProgress = progress;
    }

    if (activityText) {
        matchedFile.statusText = activityText;
    }

    syncPromptSelectedFilesState();
}

// Tab switching with forced refresh
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
        tab.classList.add('active');
        document.getElementById(tab.dataset.tab)?.classList.add('active');
        
        // Force reload data when switching tabs to ensure fresh data
        const tabName = tab.dataset.tab;
        console.log(`[TAB SWITCH] Switching to ${tabName}, forcing data refresh`);
        if (tabName === 'files') {
            loadFiles(true);
        } else if (tabName === 'prompts') {
            loadPrompts(true);
        } else if (tabName === 'reminders') {
            loadReminders(true);
        } else if (tabName === 'groups') {
            Promise.resolve();
        }

        // Groq Live polling: only active when the tab is visible
        if (tabName === 'groq-live') {
            startGroqLivePolling();
        } else {
            stopGroqLivePolling();
        }
    });
});

// Global `showMessage()` provided by `toasts.js` - do not redefine here

function applyTheme(theme) {
    if (theme === 'dark') {
        document.body.classList.add('dark');
    } else {
        document.body.classList.remove('dark');
    }
}

// Loading indicator
function showLoading(listId) {
    const list = document.getElementById(listId);
    if (!list) return; // element may not exist in the current admin view
    const loading = document.createElement('div');
    loading.className = 'loading-indicator';
    loading.id = `${listId}-loading`;
    loading.innerHTML = `<p style="text-align: center; padding: 20px; color: #2f3a3f;">${adminT('loading', 'Loading...')}</p>`;
    list.appendChild(loading);
}

function hideLoading(listId) {
    const loading = document.getElementById(`${listId}-loading`);
    if (loading) loading.remove();
}

// Files with lazy loading
async function loadFiles(reset = false) {
    if (state.files.loading) return;
    if (!reset && !state.files.hasMore) return;
    
    if (reset) {
        state.files.page = 1;
        state.files.hasMore = true;
        state.files.data = [];
        document.getElementById('files-list').innerHTML = '';
    }
    
    state.files.loading = true;
    showLoading('files-list');
    
    try {
        const cacheBust = reset ? `&cb=${Date.now()}` : '';
        const response = await fetchWithRetry(`${API_BASE}/files?page=${state.files.page}&pageSize=${state.files.pageSize}${cacheBust}`);
        const list = document.getElementById('files-list');
        
        if (!response.ok) {
            if (isTransientGatewayStatus(response.status) || response.status === 500) {
                hideLoading('files-list');

                if (reset && state.files.data.length === 0) {
                    list.innerHTML = `<div class="empty-state"><p>${adminT('serverUnavailable', 'Server is temporarily unavailable. File list will refresh automatically.')}</p></div>`;
                }

                filesDegradedRetryCount += 1;
                noteServiceOutage('files endpoint transient failure', 30);
                const retryDelayMs = getDegradedRetryDelayMs(filesDegradedRetryCount, 30);

                if (!filesDegradedRefreshTimer) {
                    filesDegradedRefreshTimer = setTimeout(() => {
                        filesDegradedRefreshTimer = null;
                        loadFiles(true).catch(err => {
                            console.warn('Automatic files refresh after transient failure failed', err);
                        });
                    }, retryDelayMs);
                }

                state.files.loading = false;
                return;
            }

            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        
        const result = await response.json();
        console.log('Files API response:', result);
        const degraded = isDegradedPagedResult(result);
        // Handle both lowercase and uppercase from API
        const filesData = result.data || result.Data || [];
        const pagination = result.pagination || result.Pagination || {};
        console.log('Files data length:', filesData.length);
        console.log('Files pagination:', pagination);
        
        hideLoading('files-list');

        if (degraded) {
            if (reset && state.files.data.length === 0) {
                list.innerHTML = `<div class="empty-state"><p>${adminT('serverBusy', 'Server is busy. File list will refresh automatically.')}</p></div>`;
            }

            filesDegradedRetryCount += 1;
            const retryAfterSeconds = Number(result.retryAfterSeconds || result.RetryAfterSeconds || 0);
            const retryDelayMs = getDegradedRetryDelayMs(filesDegradedRetryCount, retryAfterSeconds);

            if (!filesDegradedRefreshTimer) {
                filesDegradedRefreshTimer = setTimeout(() => {
                    filesDegradedRefreshTimer = null;
                    loadFiles(true).catch(err => {
                        console.warn('Automatic files refresh after degraded response failed', err);
                    });
                }, retryDelayMs);
            }

            state.files.loading = false;
            return;
        }

        filesDegradedRetryCount = 0;
        
        if (filesData && filesData.length > 0) {
            state.files.data.push(...filesData);
            
            const fragment = document.createDocumentFragment();
            filesData.forEach(file => {
                const div = createFileCard(file);
                fragment.appendChild(div);
            });
            list.appendChild(fragment);
            
            state.files.hasMore = pagination.hasNext || pagination.HasNext;
            state.files.page++;
        } else if (reset) {
            list.innerHTML = `<div class="empty-state"><p>${adminT('noUploadsYet', 'No uploads yet')}</p></div>`;
        }
        
        state.files.loading = false;
    } catch (error) {
        hideLoading('files-list');
        state.files.loading = false;
        showMessage('Error loading files: ' + error.message, 'error');
    }
}

// Prompts with lazy loading


function createFileCard(file) {
    const div = document.createElement('div');
    div.className = 'card';
    
    const id = getProp(file, 'id');
    const rawLabel = getProp(file, 'label');
    const fileName = getProp(file, 'fileName') || 'unknown';
    // Use the label only if it has a meaningful value; otherwise fall back to the file name
    const label = (rawLabel && rawLabel !== 'Untitled' && !rawLabel.startsWith('Media upload:')) ? rawLabel : (fileName !== 'unknown' ? fileName : 'Untitled');
    const fileSize = getProp(file, 'fileSize') || 0;
    const uploadedAt = getProp(file, 'uploadedAt');
    const storageKey = getProp(file, 'storageKey') || '';
    const isPending = storageKey.startsWith('PENDING:');
    const isFailed = storageKey.startsWith('FAILED:');
    const groups = getProp(file, 'groups') || [];
    
    const statusBadge = isPending 
        ? `<span class="badge" style="background:#e3f2fd;color:#1565c0;margin-left:8px;">⏳ Processing…</span>`
        : (isFailed ? `<span class="badge" style="background:#ffebee;color:#c62828;margin-left:8px;">❌ Failed</span>` : '');
    
    div.innerHTML = `
        <div class="card-header">
            <div class="card-title">${escapeHtml(label)} ${statusBadge}</div>
        </div>
        <div class="card-meta">
            File: ${escapeHtml(fileName)} | 
            Size: ${formatFileSize(fileSize)} | 
            Uploaded: ${uploadedAt ? new Date(uploadedAt).toLocaleString() : 'Just now'}
        </div>
        ${groups && groups.length > 0 ? `
            <div style="margin-top: 8px;">
                ${groups.map(g => `<span class="badge">${escapeHtml(getProp(g, 'name') || '')}</span>`).join('')}
            </div>
        ` : ''}
        <div class="card-actions">
            <button data-action="preview-file" data-file-id="${id}" data-file-name="${escapeHtml(fileName)}" class="secondary">Preview</button>
            <button class="delete" data-file-id="${id}" data-action="delete-file">Delete</button>
        </div>
    `;
    
    // Attach event listeners
    div.querySelector('[data-action="preview-file"]').addEventListener('click', (e) => {
        previewFile(e.target.dataset.fileId, e.target.dataset.fileName);
    });
    div.querySelector('[data-action="delete-file"]').addEventListener('click', (e) => {
        deleteFile(e.target.dataset.fileId);
    });
    
    return div;
}

async function refreshConversationFileCounts(items) {
    if (!Array.isArray(items) || items.length === 0) return;

    const updates = items.map(async (item) => {
        const id = getProp(item, 'id') || getProp(item, 'Id');
        if (!id) return;

        try {
            const res = await fetchWithRetry(`/api/conversations/${id}`);
            if (!res.ok) return;
            const convo = await res.json().catch(() => null);
            const messages = (convo && (convo.messages || convo.Messages)) || [];
            let totalFiles = 0;

            if (Array.isArray(messages)) {
                totalFiles = messages.reduce((sum, msg) => {
                    const rawFiles = Array.isArray(msg.files) ? msg.files
                        : (Array.isArray(msg.Files) ? msg.Files
                            : (Array.isArray(msg.Attachments)
                                ? msg.Attachments
                                : (Array.isArray(msg.attachments) ? msg.attachments : [])));
                    return sum + normalizeFileList(rawFiles).length;
                }, 0);
            }

            const badge = document.querySelector(`[data-conversation-files="${id}"]`);
            if (badge) {
                badge.textContent = String(totalFiles);
                badge.setAttribute('title', `${totalFiles} file(s) attached`);
            }
        } catch (e) {
            console.warn('Failed to refresh file count for conversation', id, e);
        }
    });

    await Promise.allSettled(updates);
}

// Auto-upload selected file from main Files tab
const mainFileInput = document.getElementById('file-input');
if (mainFileInput) {
    mainFileInput.addEventListener('change', async (e) => {
        const file = e.target.files[0];
        if (!file) return;

        const formData = new FormData();
        formData.append('file', file);
        // Auto-generate label from filename
        formData.append('label', file.name || 'Untitled');

        // Retry on transient server errors (500 / 502 / 503) up to 3 attempts.
        const MAX_UPLOAD_ATTEMPTS = 3;
        let lastError = null;
        let uploadedFileId = null;

        showMessage('Uploading file…', 'info');

        for (let attempt = 1; attempt <= MAX_UPLOAD_ATTEMPTS; attempt++) {
            try {
                if (attempt > 1) {
                    const backoffMs = attempt * 1500;
                    showMessage(`Upload attempt ${attempt} of ${MAX_UPLOAD_ATTEMPTS}…`, 'info');
                    await new Promise(r => setTimeout(r, backoffMs));
                }

                const response = await fetch(`${API_BASE}/files`, {
                    method: 'POST',
                    body: formData
                });

                if (response.ok || response.status === 202) {
                    const data = await response.json().catch(() => ({}));
                    uploadedFileId = data.id || data.Id || null;
                    lastError = null;
                    break; // success
                }

                const status = response.status;
                // Retry on transient errors; bail immediately on client errors (4xx).
                if (status >= 400 && status < 500) {
                    let msg = 'Error uploading file';
                    try { const err = await response.json(); msg = err.detail || err.title || msg; } catch (_) {}
                    showMessage(msg, 'error');
                    mainFileInput.value = '';
                    return;
                }

                lastError = `Server error ${status}`;
            } catch (err) {
                lastError = err.message || 'Network error';
            }
        }

        if (lastError) {
            showMessage(`Error uploading file: ${lastError}`, 'error');
            mainFileInput.value = '';
            return;
        }

        mainFileInput.value = '';
        showMessage('File uploaded — processing…', 'success');
        await loadFiles(true);


        // Poll for background processing completion (up to ~30 s).
        if (uploadedFileId) {
            const POLL_INTERVAL_MS = 2000;
            const POLL_TIMEOUT_MS = 30000;
            const pollStart = Date.now();
            let ready = false;

            while (Date.now() - pollStart < POLL_TIMEOUT_MS) {
                await new Promise(r => setTimeout(r, POLL_INTERVAL_MS));
                try {
                    const cacheBuster = `${Date.now()}`;
                    const statusRes = await fetch(`${API_BASE}/files/${uploadedFileId}/status?v=${cacheBuster}`, {
                        headers: { 'Cache-Control': 'no-cache' }
                    });
                    if (statusRes.ok) {
                        const statusData = await statusRes.json().catch(() => null);
                        if (statusData?.failed) {
                            showMessage('File processing failed. The file may not be usable.', 'error');
                            break;
                        }
                        if (statusData?.isReady && !statusData?.pending) {
                            ready = true;
                            break;
                        }
                    }
                } catch (_) { /* keep polling */ }
            }

            if (ready) {
                showMessage('File ready.', 'success');
                await loadFiles(true);
            }
        }
    });
}

// Enter key handler for file label input
document.getElementById('file-label')?.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') {
        e.preventDefault();
        const uploadForm = document.getElementById('upload-form');
        if (uploadForm) {
            uploadForm.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
        }
    }
});

async function deleteFile(id) {
    if (!confirm('Are you sure you want to delete this file?')) return;
    
    try {
        const response = await fetch(`${API_BASE}/files/${id}`, {
            method: 'DELETE'
        });
        
        if (response.ok || response.status === 204) {
            showMessage('File deleted successfully');
            await loadFiles(true);

        } else {
            const error = await response.json().catch(() => null);
            showMessage(error?.detail || 'Error deleting file', 'error');
        }
    } catch (error) {
        showMessage('Error deleting file: ' + error.message, 'error');
    }
}

// Store current preview file info
let currentPreviewFile = { id: null, fileName: null };

const TRANSIENT_GATEWAY_STATUSES = new Set([502, 503, 504]);
const PRESIGNED_COMPLETE_TIMEOUT_MS = 70000;
let lastUploadPollingOutageNoticeAt = 0;
let serviceOutageBackoffAttempt = 0;
let serviceOutageUntilMs = 0;
let lastServiceOutageNoticeAt = 0;
let filesDegradedRefreshTimer = null;
let groupsDegradedRefreshTimer = null;
let filesDegradedRetryCount = 0;
let groupsDegradedRetryCount = 0;

function delay(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

async function fetchWithTimeout(url, options = {}, timeoutMs = 8000) {
    const controller = new AbortController();
    const safeTimeout = Math.max(1000, Number(timeoutMs) || 8000);
    const timeoutId = setTimeout(() => controller.abort(), safeTimeout);

    try {
        return await fetch(url, {
            ...options,
            signal: controller.signal
        });
    } finally {
        clearTimeout(timeoutId);
    }
}

function isTransientGatewayStatus(status) {
    return TRANSIENT_GATEWAY_STATUSES.has(Number(status));
}

function getRetryDelayMs(baseIntervalMs, consecutiveFailures, maxDelayMs = 20000) {
    const safeBase = Math.max(500, Number(baseIntervalMs) || 1500);
    const cappedFailures = Math.min(8, Math.max(0, Number(consecutiveFailures) || 0));
    const backoff = Math.min(maxDelayMs, Math.round(safeBase * Math.pow(1.6, cappedFailures)));
    const jitter = Math.floor(Math.random() * Math.max(250, Math.round(backoff * 0.2)));
    return backoff + jitter;
}

function getDegradedRetryDelayMs(retryCount, retryAfterSeconds = 0) {
    const retryBaseMs = Math.max(20000, Number(retryAfterSeconds || 0) * 1000);
    const autoBackoffMs = getRetryDelayMs(20000, retryCount, 120000);
    return Math.max(retryBaseMs, autoBackoffMs);
}

function getServiceOutageCooldownMs() {
    return Math.max(0, Number(serviceOutageUntilMs || 0) - Date.now());
}

function isServiceUnavailableError(error) {
    const text = String(error?.message || error || '').toLowerCase();
    return text.includes('503')
        || text.includes('service unavailable')
        || text.includes('failed to complete negotiation')
        || text.includes('failed to start the connection');
}

function noteServiceOutage(source = '', retryAfterSeconds = 0) {
    serviceOutageBackoffAttempt = Math.min(serviceOutageBackoffAttempt + 1, 10);
    const baseRetryMs = Math.max(15000, Number(retryAfterSeconds || 0) * 1000);
    const retryDelayMs = getRetryDelayMs(baseRetryMs, serviceOutageBackoffAttempt, 180000);
    serviceOutageUntilMs = Math.max(serviceOutageUntilMs, Date.now() + retryDelayMs);

    const now = Date.now();
    if (typeof showMessage === 'function' && (now - lastServiceOutageNoticeAt) >= 30000) {
        lastServiceOutageNoticeAt = now;
        showMessage('Server is temporarily unavailable. Continuing retries in the background.', 'info');
    }

    if (source) {
        console.warn(`[OUTAGE] ${source}; cooling down for ${retryDelayMs}ms`);
    }

    return retryDelayMs;
}

function clearServiceOutage(source = '') {
    if (serviceOutageUntilMs <= 0 && serviceOutageBackoffAttempt === 0) {
        return;
    }

    serviceOutageBackoffAttempt = 0;
    serviceOutageUntilMs = 0;

    if (source) {
        console.log(`[OUTAGE] Cleared by ${source}`);
    }
}

function isDegradedPagedResult(result) {
    if (!result || typeof result !== 'object') {
        return false;
    }

    const degraded = result.degraded ?? result.Degraded ?? false;
    const error = String(result.error || result.Error || '').trim().toLowerCase();
    return degraded === true || error === 'temporary_unavailable';
}

function createClientGuid() {
    if (window.crypto && typeof window.crypto.randomUUID === 'function') {
        return window.crypto.randomUUID();
    }

    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, char => {
        const random = Math.floor(Math.random() * 16);
        const value = char === 'x' ? random : ((random & 0x3) | 0x8);
        return value.toString(16);
    });
}

async function registerPresignedUpload(payload, maxAttempts = 4) {
    let lastError = null;

    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        try {
            const response = await fetchWithTimeout(`${API_BASE}/files/complete`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            }, PRESIGNED_COMPLETE_TIMEOUT_MS);

            if (response.ok) {
                clearServiceOutage('upload registration');
                return await response.json();
            }

            if (response.status === 503) {
                noteServiceOutage('upload registration 503');
            }

            if (isTransientGatewayStatus(response.status) && attempt < maxAttempts) {
                await delay(getRetryDelayMs(1500, attempt));
                continue;
            }

            throw new Error(`Failed to register upload (${response.status})`);
        } catch (error) {
            lastError = error;
            const isRetryableError = error?.name === 'AbortError'
                || /network/i.test(String(error?.message || ''))
                || /502|503|504|timeout|timed out|aborted/.test(String(error?.message || ''));

            if (isRetryableError) {
                noteServiceOutage('upload registration transient error');
            }

            if (isRetryableError && attempt < maxAttempts) {
                await delay(getRetryDelayMs(1500, attempt));
                continue;
            }

            break;
        }
    }

    if (lastError?.name === 'AbortError') {
        throw new Error(`Upload registration timed out after ${Math.round(PRESIGNED_COMPLETE_TIMEOUT_MS / 1000)}s`);
    }

    throw lastError || new Error('Failed to register upload');
}

function notifyUploadPollingOutage() {
    // Do NOT feed into the global service-outage backoff here — upload-polling DB timeouts
    // are separate from API/hub availability and should not delay hub reconnects.
    // (503 responses on the status endpoint call noteServiceOutage directly instead.)
    const now = Date.now();
    if ((now - lastUploadPollingOutageNoticeAt) < 30000) {
        return;
    }

    lastUploadPollingOutageNoticeAt = now;
    if (typeof showMessage === 'function') {
        showMessage('Server temporarily unavailable. Waiting before retrying file readiness checks.', 'info');
    }
}

async function waitForPreviewReady(id, timeoutMs = 15000, intervalMs = 1000) {
    // Use the lightweight /status endpoint (DB-only check) rather than /info which
    // downloads the first bytes from object storage and fails when storage is slow.
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        try {
            const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
            const res = await fetchWithTimeout(
                `${API_BASE}/files/${id}/status?v=${cacheBuster}`,
                { headers: { 'Cache-Control': 'no-cache' } },
                5000
            );
            if (res.ok) {
                const data = await res.json().catch(() => null);
                // isReady true and not pending/failed → file is ready for preview
                if (data && data.isReady && !data.pending && !data.failed) return true;
                // Processing has definitively failed; no point waiting further
                if (data && data.failed) return false;
            }
        } catch (error) {
            // Keep polling until timeout.
        }

        await delay(intervalMs);
    }

    return false;
}

async function waitForDocumentReady(id, timeoutMs = 300000, intervalMs = 2000) {
    const start = Date.now();
    let consecutiveTransientFailures = 0;
    let hadTransientFailures = false;
    const requestTimeoutMs = Math.max(5000, intervalMs * 4);

    while (Date.now() - start < timeoutMs) {
        let response = null;

        try {
            const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
            response = await fetchWithTimeout(`/api/documents/${id}?v=${cacheBuster}`, {}, requestTimeoutMs);
            if (response.ok) {
                consecutiveTransientFailures = 0;
                const documentMeta = await response.json().catch(() => null);
                const fileSize = Number(documentMeta?.fileSize || documentMeta?.FileSize || 0);
                const isProcessed = documentMeta?.isProcessed ?? documentMeta?.IsProcessed ?? false;
                if (fileSize > 0 && isProcessed) {
                    return {
                        documentId: documentMeta?.id || documentMeta?.Id || id,
                        managedFileId: documentMeta?.managedFileId || documentMeta?.ManagedFileId || null,
                        fileName: documentMeta?.fileName || documentMeta?.FileName || null
                    };
                }
            } else if (isTransientGatewayStatus(response.status)) {
                hadTransientFailures = true;
                consecutiveTransientFailures++;
                notifyUploadPollingOutage();
                await delay(getRetryDelayMs(intervalMs, consecutiveTransientFailures));
                continue;
            } else {
                consecutiveTransientFailures = 0;
            }
        } catch (error) {
            if (error?.name === 'AbortError') {
                notifyUploadPollingOutage();
            }
            hadTransientFailures = true;
            consecutiveTransientFailures++;
            await delay(getRetryDelayMs(intervalMs, consecutiveTransientFailures));
            continue;
        }

        await delay(intervalMs);
    }

    return hadTransientFailures ? { unavailable: true } : null;
}

async function waitForManagedFileReady(id, timeoutMs = 120000, intervalMs = 1500) {
    const start = Date.now();
    let consecutiveTransientFailures = 0;
    let hadTransientFailures = false;
    const requestTimeoutMs = Math.max(5000, intervalMs * 4);
    const getDeferredRetryMs = () => getRetryDelayMs(Math.max(intervalMs, 15000), consecutiveTransientFailures, 90000);

    while (Date.now() - start < timeoutMs) {
        let response = null;

        try {
            const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
            response = await fetchWithTimeout(`${API_BASE}/files/${id}/status?v=${cacheBuster}`, {}, requestTimeoutMs);
            if (response.ok) {
                const fileMeta = await response.json().catch(() => null);
                if (!fileMeta) {
                    hadTransientFailures = true;
                    consecutiveTransientFailures++;
                    if (consecutiveTransientFailures >= MAX_UPLOAD_STATUS_TRANSIENTS_BEFORE_DEFER) {
                        return { deferred: true, retryAfterMs: getDeferredRetryMs() };
                    }
                    await delay(getRetryDelayMs(intervalMs, consecutiveTransientFailures));
                    continue;
                }

                const failed = fileMeta?.failed ?? false;
                const isReady = fileMeta?.isReady ?? false;
                const pending = fileMeta?.pending ?? false;
                const hasStorageKey = fileMeta?.hasStorageKey;
                const statusError = String(fileMeta?.error || '').toLowerCase();

                if (failed) {
                    return {
                        failed: true,
                        error: fileMeta?.error || 'Upload failed on server.'
                    };
                }

                if (isReady) {
                    return {
                        failed: false,
                        id: fileMeta?.id || id,
                        fileName: fileMeta?.fileName || fileMeta?.label || null
                    };
                }

                const isBusyTimeout = pending
                    && (statusError.includes('timed out') || statusError.includes('server is busy'));
                const isRegistrationLag = pending && hasStorageKey === false;
                if (isBusyTimeout || isRegistrationLag) {
                    hadTransientFailures = true;
                    consecutiveTransientFailures++;
                    notifyUploadPollingOutage();
                    if (consecutiveTransientFailures >= MAX_UPLOAD_STATUS_TRANSIENTS_BEFORE_DEFER) {
                        return { deferred: true, retryAfterMs: getDeferredRetryMs() };
                    }
                    await delay(getRetryDelayMs(Math.max(intervalMs, 6000), consecutiveTransientFailures, 30000));
                    continue;
                }

                if (pending) {
                    hadTransientFailures = true;
                    consecutiveTransientFailures++;
                    if (consecutiveTransientFailures >= MAX_UPLOAD_STATUS_TRANSIENTS_BEFORE_DEFER) {
                        return { deferred: true, retryAfterMs: getDeferredRetryMs() };
                    }
                    const pendingDelayMs = getRetryDelayMs(Math.max(intervalMs, 6000), consecutiveTransientFailures, 45000);
                    await delay(pendingDelayMs);
                    continue;
                }
            } else if (isTransientGatewayStatus(response.status)) {
                hadTransientFailures = true;
                consecutiveTransientFailures++;
                // A genuine gateway/rate-limit error on the status endpoint IS a service outage signal.
                noteServiceOutage(`file status ${response.status}`);
                notifyUploadPollingOutage();
                if (consecutiveTransientFailures >= MAX_UPLOAD_STATUS_TRANSIENTS_BEFORE_DEFER) {
                    return { deferred: true, retryAfterMs: getDeferredRetryMs() };
                }
                await delay(getRetryDelayMs(intervalMs, consecutiveTransientFailures));
                continue;
            } else if (response.status === 404) {
                hadTransientFailures = true;
                consecutiveTransientFailures++;
                if (consecutiveTransientFailures >= MAX_UPLOAD_STATUS_TRANSIENTS_BEFORE_DEFER) {
                    return { deferred: true, retryAfterMs: getDeferredRetryMs() };
                }
                await delay(getRetryDelayMs(intervalMs, consecutiveTransientFailures, 25000));
                continue;
            } else {
                consecutiveTransientFailures = 0;
            }
        } catch (error) {
            if (error?.name === 'AbortError') {
                notifyUploadPollingOutage();
            }
            hadTransientFailures = true;
            consecutiveTransientFailures++;
            if (consecutiveTransientFailures >= MAX_UPLOAD_STATUS_TRANSIENTS_BEFORE_DEFER) {
                return { deferred: true, retryAfterMs: getDeferredRetryMs() };
            }
            await delay(getRetryDelayMs(intervalMs, consecutiveTransientFailures));
            continue;
        }

        await delay(intervalMs);
    }

    return hadTransientFailures ? { unavailable: true } : null;
}

function getManagedFileDisplayName(file) {
    if (!file) {
        return '';
    }

    const rawLabel = String(getProp(file, 'label') || '').trim();
    if (rawLabel && rawLabel.toLowerCase() !== 'untitled') {
        return rawLabel;
    }

    return String(getProp(file, 'fileName') || '').trim();
}

function findManagedFileMatchBySignature(filesData, expectedName, expectedSizeBytes, earliestUploadMs) {
    if (!Array.isArray(filesData) || filesData.length === 0) {
        return null;
    }

    for (const file of filesData) {
        const candidateName = normalizeUploadFileName(getManagedFileDisplayName(file));
        if (!candidateName || candidateName !== expectedName) {
            continue;
        }

        if (Number.isFinite(expectedSizeBytes) && expectedSizeBytes > 0) {
            const candidateSize = Number(getProp(file, 'fileSize') || 0);
            if (Number.isFinite(candidateSize) && candidateSize > 0 && candidateSize !== expectedSizeBytes) {
                continue;
            }
        }

        const uploadedAtRaw = getProp(file, 'uploadedAt');
        if (uploadedAtRaw) {
            const uploadedAtMs = Date.parse(uploadedAtRaw);
            if (Number.isFinite(uploadedAtMs) && uploadedAtMs < earliestUploadMs) {
                continue;
            }
        }

        const managedFileId = getProp(file, 'id');
        if (!managedFileId) {
            continue;
        }

        return {
            managedFileId,
            id: managedFileId,
            fileName: getManagedFileDisplayName(file)
        };
    }

    return null;
}

async function waitForManagedFileReference(expectedName, expectedSizeBytes = null, timeoutMs = 180000, intervalMs = 2000) {
    const normalizedExpectedName = normalizeUploadFileName(expectedName);
    if (!normalizedExpectedName) {
        return { notFound: true };
    }

    const start = Date.now();
    const earliestUploadMs = start - 60000;
    const requestTimeoutMs = Math.max(5000, intervalMs * 4);
    const normalizedExpectedSize = Number.isFinite(Number(expectedSizeBytes)) ? Number(expectedSizeBytes) : null;
    let consecutiveTransientFailures = 0;
    let hadTransientFailures = false;

    while (Date.now() - start < timeoutMs) {
        let response = null;

        try {
            const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
            response = await fetchWithTimeout(`${API_BASE}/files?page=1&pageSize=25&v=${cacheBuster}`, {
                headers: {
                    'Cache-Control': 'no-cache'
                }
            }, requestTimeoutMs);

            if (response.ok) {
                consecutiveTransientFailures = 0;
                const payload = await response.json().catch(() => null);
                const filesData = (payload && (payload.data || payload.Data)) || [];
                const matched = findManagedFileMatchBySignature(filesData, normalizedExpectedName, normalizedExpectedSize, earliestUploadMs);
                if (matched) {
                    return matched;
                }
            } else if (isTransientGatewayStatus(response.status)) {
                hadTransientFailures = true;
                consecutiveTransientFailures++;
                notifyUploadPollingOutage();
                await delay(getRetryDelayMs(intervalMs, consecutiveTransientFailures));
                continue;
            } else {
                consecutiveTransientFailures = 0;
            }
        } catch (error) {
            if (error?.name === 'AbortError') {
                notifyUploadPollingOutage();
            }
            hadTransientFailures = true;
            consecutiveTransientFailures++;
            await delay(getRetryDelayMs(intervalMs, consecutiveTransientFailures));
            continue;
        }

        await delay(intervalMs);
    }

    if (hadTransientFailures) {
        return { unavailable: true };
    }

    return { notFound: true };
}

function syncPromptSelectedFilesState() {
    window.promptSelectedFiles = promptSelectedFiles;
    updatePromptFilesList();
}

async function monitorPromptFileAvailability(fileObj, timeoutMs = 300000, intervalMs = 2000) {
    if (!fileObj || fileObj._availabilityMonitorStarted) return;

    fileObj._availabilityMonitorStarted = true;
    try {
        let ready = null;
        const expectedFileName = fileObj.file?.name || fileObj.name || '';
        const expectedFileSize = Number(fileObj.expectedSize || fileObj.file?.size || 0);

        if (fileObj.isManagedFile && fileObj.id) {
            ready = await waitForManagedFileReady(fileObj.id, timeoutMs, intervalMs);
        } else if (fileObj.documentId || fileObj.id) {
            ready = await waitForDocumentReady(fileObj.documentId || fileObj.id, timeoutMs, intervalMs);
        } else if (expectedFileName) {
            const managedFileRef = await waitForManagedFileReference(expectedFileName, expectedFileSize || null, timeoutMs, intervalMs);
            if (managedFileRef?.managedFileId) {
                fileObj.id = managedFileRef.managedFileId;
                fileObj.managedFileId = managedFileRef.managedFileId;
                fileObj.isManagedFile = true;
                fileObj.name = managedFileRef.fileName || expectedFileName;
                fileObj.statusText = 'Finishing upload...';
                fileObj.uploadProgress = 100;
                syncPromptSelectedFilesState();

                ready = await waitForManagedFileReady(managedFileRef.managedFileId, timeoutMs, intervalMs);
            } else if (managedFileRef?.unavailable) {
                ready = { unavailable: true };
            } else if (managedFileRef?.notFound) {
                ready = {
                    failed: true,
                    error: `Unable to confirm upload for "${expectedFileName}". Please retry the upload.`
                };
            }
        }

        if (!promptSelectedFiles.includes(fileObj)) {
            return;
        }

        if (!fileObj.pending) {
            fileObj._availabilityMonitorStarted = false;
            return;
        }

        if (ready?.failed) {
            fileObj._pollDeferredCount = 0;
            fileObj._pollDeferredNotified = false;
            promptSelectedFiles = promptSelectedFiles.filter(f => f !== fileObj);
            syncPromptSelectedFilesState();
            showMessage(ready.error || 'File upload failed on the server.', 'error');
            return;
        }

        if (ready?.deferred) {
            const deferredCount = Number(fileObj._pollDeferredCount || 0) + 1;
            fileObj._pollDeferredCount = deferredCount;

            // After too many deferrals the file is presumed permanently stuck on the server.
            // Tell the user to re-upload rather than looping forever.
            if (deferredCount >= MAX_POLL_DEFERRED_BEFORE_ABANDON) {
                fileObj._pollDeferredCount = 0;
                fileObj._pollDeferredNotified = false;
                fileObj._availabilityMonitorStarted = false;
                promptSelectedFiles = promptSelectedFiles.filter(f => f !== fileObj);
                syncPromptSelectedFilesState();
                const fileLabel = fileObj.name || fileObj.file?.name || 'The file';
                showMessage(`"${fileLabel}" could not be confirmed after repeated retries. Please remove and re-upload it.`, 'error');
                return;
            }

            const requestedDelayMs = Math.max(0, Number(ready.retryAfterMs) || 0);
            const autoDelayMs = getRetryDelayMs(
                Math.max(intervalMs, MIN_UPLOAD_STATUS_DEFER_RETRY_MS),
                deferredCount,
                MAX_UPLOAD_STATUS_DEFER_RETRY_MS
            );
            const resumeDelayMs = Math.max(MIN_UPLOAD_STATUS_DEFER_RETRY_MS, requestedDelayMs, autoDelayMs);

            fileObj.statusText = 'Processing in background. Live updates will finish this upload.';
            fileObj.uploadProgress = 100;
            fileObj._availabilityMonitorStarted = false;
            syncPromptSelectedFilesState();

            if (!fileObj._pollDeferredNotified) {
                fileObj._pollDeferredNotified = true;
                showMessage('Upload is still processing. Switched to background checks while waiting for live updates.', 'info');
            }

            setTimeout(() => {
                if (!promptSelectedFiles.includes(fileObj)) {
                    return;
                }

                if (fileObj.pending && !fileObj._availabilityMonitorStarted) {
                    monitorPromptFileAvailability(fileObj, timeoutMs, intervalMs).catch(retryError => {
                        console.warn('Prompt file availability monitor retry failed', retryError);
                    });
                }
            }, resumeDelayMs);
            return;
        }

        if (ready?.unavailable) {
            fileObj.statusText = 'Server temporarily unavailable. Waiting to resume checks...';
            fileObj.uploadProgress = 100;
            fileObj._availabilityMonitorStarted = false;
            syncPromptSelectedFilesState();

            const resumeDelayMs = Math.max(12000, intervalMs * 6);
            setTimeout(() => {
                if (!promptSelectedFiles.includes(fileObj)) {
                    return;
                }

                if (fileObj.pending && !fileObj._availabilityMonitorStarted) {
                    monitorPromptFileAvailability(fileObj, timeoutMs, intervalMs).catch(retryError => {
                        console.warn('Prompt file availability monitor retry failed', retryError);
                    });
                }
            }, resumeDelayMs);
            return;
        }

        if (ready) {
            fileObj.pending = false;
            fileObj._pollDeferredCount = 0;
            fileObj._pollDeferredNotified = false;
            fileObj.statusText = '';
            fileObj.uploadProgress = 100;
            fileObj.documentId = ready.documentId || fileObj.documentId;
            fileObj.managedFileId = ready.managedFileId || fileObj.managedFileId;
            fileObj.name = ready.fileName || fileObj.name;
            fileObj._availabilityMonitorStarted = false;
            syncPromptSelectedFilesState();

            try {
                await loadFiles(true);
            } catch (error) {
                console.warn('Failed to refresh files after prompt upload became ready', error);
            }
            return;
        }

        fileObj.statusText = 'Still processing on server...';
        fileObj.uploadProgress = 100;
        fileObj._availabilityMonitorStarted = false;
        syncPromptSelectedFilesState();

        const resumeDelayMs = Math.max(10000, intervalMs * 5);
        setTimeout(() => {
            if (!promptSelectedFiles.includes(fileObj)) {
                return;
            }

            if (fileObj.pending && !fileObj._availabilityMonitorStarted) {
                monitorPromptFileAvailability(fileObj, timeoutMs, intervalMs).catch(retryError => {
                    console.warn('Prompt file availability monitor retry failed', retryError);
                });
            }
        }, resumeDelayMs);
    } catch (error) {
        if (!promptSelectedFiles.includes(fileObj)) {
            return;
        }

        fileObj.statusText = 'Server temporarily unavailable. Waiting to resume checks...';
        fileObj.uploadProgress = 100;
        fileObj._availabilityMonitorStarted = false;
        syncPromptSelectedFilesState();

        const resumeDelayMs = Math.max(12000, intervalMs * 6);
        setTimeout(() => {
            if (!promptSelectedFiles.includes(fileObj)) {
                return;
            }

            if (fileObj.pending && !fileObj._availabilityMonitorStarted) {
                monitorPromptFileAvailability(fileObj, timeoutMs, intervalMs).catch(retryError => {
                    console.warn('Prompt file availability monitor retry failed', retryError);
                });
            }
        }, resumeDelayMs);
    }
}

window.previewPromptSelectedFile = function(index) {
    const fileObj = promptSelectedFiles[index];
    if (!fileObj || fileObj.pending) return;

    const fileName = fileObj.file?.name || fileObj.name || 'Document';
    if (fileObj.isManagedFile) {
        const managedFileId = fileObj.managedFileId || fileObj.id;
        if (managedFileId) {
            previewFile(managedFileId, fileName);
        }
        return;
    }

    const documentId = fileObj.documentId || fileObj.id;
    if (documentId) {
        previewDocumentFile(documentId, fileName);
    }
};

function parseRequestedFileNumber(input) {
    if (!input) return null;
    const indexed = input.match(/\b(?:file|document)\s*(?:number\s*)?(\d{1,2})\b/i);
    if (indexed && indexed[1]) {
        const num = Number.parseInt(indexed[1], 10);
        return Number.isFinite(num) && num > 0 ? num : null;
    }

    const ordinals = [
        ['first', 1],
        ['one', 1],
        ['second', 2],
        ['two', 2],
        ['third', 3],
        ['three', 3],
        ['fourth', 4],
        ['four', 4],
        ['fifth', 5],
        ['five', 5]
    ];
    for (const [word, number] of ordinals) {
        if (new RegExp(`\\b${word}\\b`, 'i').test(input)) {
            return number;
        }
    }

    const fallback = input.match(/\b(\d{1,2})\b/);
    if (fallback && fallback[1]) {
        const num = Number.parseInt(fallback[1], 10);
        return Number.isFinite(num) && num > 0 ? num : null;
    }

    return null;
}

function clearPendingRecentFileChoices() {
    pendingRecentFileChoices = null;
    pendingRecentFileChoicesExpiresAt = 0;
}

function setPendingRecentFileChoices(files) {
    if (!Array.isArray(files) || files.length === 0) {
        clearPendingRecentFileChoices();
        return;
    }

    pendingRecentFileChoices = files.slice();
    pendingRecentFileChoicesExpiresAt = Date.now() + RECENT_FILE_SELECTION_WINDOW_MS;
}

function isRecentFileSelectionIntent(input) {
    const text = String(input || '').trim();
    if (!text) {
        return false;
    }

    if (/^\d{1,2}$/.test(text)) {
        return true;
    }

    if (/^(?:the\s+)?(?:first|second|third|fourth|fifth|one|two|three|four|five)(?:\s+(?:one|file|document))?$/i.test(text)) {
        return true;
    }

    if (/\b(?:file|document)\s*(?:number\s*)?\d{1,2}\b/i.test(text)) {
        return true;
    }

    const hasActionWord = /\b(open|preview|show|select|pick)\b/i.test(text);
    const hasFileWord = /\b(file|document)\b/i.test(text);
    return hasActionWord && hasFileWord;
}

function resolvePendingRecentFileChoice(input) {
    if (!Array.isArray(pendingRecentFileChoices) || pendingRecentFileChoices.length === 0) {
        return { state: 'none' };
    }

    if (pendingRecentFileChoicesExpiresAt && Date.now() > pendingRecentFileChoicesExpiresAt) {
        clearPendingRecentFileChoices();
        return { state: 'expired' };
    }

    if (!isRecentFileSelectionIntent(input)) {
        return { state: 'none' };
    }

    const requestedNumber = parseRequestedFileNumber(input);
    if (requestedNumber === null) {
        return { state: 'needs-number', max: pendingRecentFileChoices.length };
    }

    const idx = requestedNumber - 1;
    if (idx < 0 || idx >= pendingRecentFileChoices.length) {
        return {
            state: 'out-of-range',
            requested: requestedNumber,
            max: pendingRecentFileChoices.length
        };
    }

    return {
        state: 'selected',
        requested: requestedNumber,
        file: pendingRecentFileChoices[idx]
    };
}

function isDirectRecentDocumentPreviewCommand(input) {
    const text = String(input || '').trim().toLowerCase().replace(/\s+/g, ' ');
    if (!text) {
        return false;
    }

    return /^(please\s+)?(select|open|preview|show)\s+(the\s+)?(latest|last|most recent|recent)?\s*(document|file)\s*\.?$/.test(text)
        || /^(please\s+)?select\s+document\s*\.?$/.test(text)
        || /^(please\s+)?open\s+last\s+document\s*\.?$/.test(text)
        || /^(please\s+)?open\s+latest\s+document\s*\.?$/.test(text);
}

function renderRecentFilesAction(responseContent, recentFiles) {
    if (!responseContent) {
        return;
    }

    const listHtml = recentFiles
        .map((file, idx) => {
            const uploadedLabel = file.uploadedAt
                ? new Date(file.uploadedAt).toLocaleString()
                : 'Unknown time';
            return `
                <li style="margin-bottom:8px; display:flex; justify-content:space-between; gap:10px; align-items:center;">
                    <span><strong>${idx + 1}.</strong> ${escapeHtml(file.fileName)}<br><small style="color:#7f8c8d;">${escapeHtml(uploadedLabel)}</small></span>
                    <button type="button" data-action-open-index="${idx}" style="padding:4px 10px; border:none; border-radius:4px; background:#0b63a6; color:#fff; cursor:pointer;">Open</button>
                </li>
            `;
        })
        .join('');

    const hint = `<p style="margin: 8px 0 0; color:#2f3a3f;">Say a number (for example <strong>2</strong> or <strong>open file 2</strong>) or click Open below.</p>`;

    responseContent.innerHTML = `
        <div style="padding: 15px; white-space: normal;">
            <div style="font-weight: 600; margin-bottom: 10px;">Recent files</div>
            <ol style="margin:0; padding-left:20px;">${listHtml}</ol>
            ${hint}
        </div>
    `;

    responseContent.querySelectorAll('button[data-action-open-index]').forEach(btn => {
        btn.addEventListener('click', async () => {
            const idx = Number.parseInt(btn.getAttribute('data-action-open-index') || '-1', 10);
            if (Number.isFinite(idx) && idx >= 0 && idx < recentFiles.length) {
                const selected = recentFiles[idx];
                const opened = await openFileFromAction(selected.id, selected.fileName, responseContent);
                if (opened) {
                    clearPendingRecentFileChoices();
                }
            }
        });
    });
}

async function openMostRecentDocumentPreview(responseContent) {
    try {
        const recentFiles = await fetchRecentFilesForAction(5);
        if (!recentFiles.length) {
            clearPendingRecentFileChoices();
            if (responseContent) {
                responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;">${adminT('noRecentFiles', 'No recent files found. Upload one first, then try again.')}</div>`;
            }
            return true;
        }

        setPendingRecentFileChoices(recentFiles);
        const primary = recentFiles[0];
        const opened = await openFileFromAction(primary.id, primary.fileName, responseContent);
        if (opened) {
            clearPendingRecentFileChoices();
        } else {
            renderRecentFilesAction(responseContent, recentFiles);
        }

        return true;
    } catch (error) {
        clearPendingRecentFileChoices();
        if (responseContent) {
            responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap; color:#e74c3c;">Could not load recent files right now.</div>';
        }
        return false;
    }
}

async function fetchRecentFilesForAction(limit = 5) {
    const pageSize = Math.max(limit, 10);
    const response = await fetchWithRetry(`${API_BASE}/files?page=1&pageSize=${pageSize}`);
    if (!response.ok) {
        throw new Error(`Failed to load recent files (${response.status})`);
    }

    const result = await response.json().catch(() => ({}));
    const filesData = result.data || result.Data || [];

    return filesData
        .map(file => ({
            id: getProp(file, 'id'),
            fileName: getProp(file, 'label') || getProp(file, 'fileName') || 'Untitled file',
            uploadedAt: getProp(file, 'uploadedAt') || getProp(file, 'UploadedAt') || null
        }))
        .filter(file => file.id)
        .slice(0, limit);
}

async function getFileStatusForAction(fileId) {
    if (!fileId) {
        return null;
    }

    const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
    try {
        const response = await fetchWithTimeout(`${API_BASE}/files/${fileId}/status?v=${cacheBuster}`, {
            headers: {
                'Cache-Control': 'no-cache'
            }
        }, 8000);

        if (!response.ok) {
            return null;
        }

        return await response.json().catch(() => null);
    } catch (error) {
        return null;
    }
}

async function openFileFromAction(fileId, fileName, responseContent) {
    const filesTab = document.querySelector('[data-tab="files"]') || document.getElementById('files-tab');
    if (filesTab) filesTab.click();

    const status = await getFileStatusForAction(fileId);
    if (status?.failed) {
        if (responseContent) {
            responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap; color:#e74c3c;">Unable to open <strong>${escapeHtml(fileName || 'selected file')}</strong>. Upload failed on the server.</div>`;
        }
        return false;
    }

    if (status && (status.pending || status.isReady === false)) {
        if (responseContent) {
            responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap; color:#e67e22;"><strong>${escapeHtml(fileName || 'Selected file')}</strong> is still processing. Choose another recent file or try again shortly.</div>`;
        }
        return false;
    }

    const previewReady = await waitForPreviewReady(fileId, 15000, 1000);
    if (!previewReady) {
        if (responseContent) {
            responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap; color:#e74c3c;">Unable to open preview right now. The file may still be processing.</div>';
        }
        return false;
    }

    if (responseContent) {
        responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;">Opening <strong>${escapeHtml(fileName || 'selected file')}</strong>...</div>`;
    }

    setTimeout(() => previewFile(fileId, fileName || 'selected file'), 200);
    return true;
}

async function previewFile(id, fileName) {
    const previewContainer = document.getElementById('file-preview');
    const previewContent = document.getElementById('preview-content');
    
    // Store file info for download button
    currentPreviewFile = { id, fileName };
    
    previewContent.innerHTML = `<p style="color: #2f3a3f;">${adminT('loadingPreview', 'Loading preview...')}</p>`;
    previewContainer.style.display = 'block';
    
    // Scroll to preview
    previewContainer.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    
    const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
    const previewUrl = `${API_BASE}/files/${id}/preview?v=${cacheBuster}`;
    const ext = fileName.split('.').pop().toLowerCase();

    if (previewContent?.dataset?.blobUrl) {
        try {
            URL.revokeObjectURL(previewContent.dataset.blobUrl);
        } catch (e) {
            console.warn('Failed to revoke preview blob URL', e);
        }
        delete previewContent.dataset.blobUrl;
    }
    // Fetch file once and decide preview based on MIME/header rather than filename extension
    try {
        const response = await fetch(previewUrl);
        if (!response.ok) throw new Error('Failed to load file');
        const contentType = (response.headers.get('content-type') || '').toLowerCase();
        const buffer = await response.arrayBuffer();
        const header = new TextDecoder().decode(new Uint8Array(buffer.slice(0, 4)));

        // Images
        if (contentType.startsWith('image/') || ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg'].includes(ext)) {
            const blobUrl = URL.createObjectURL(new Blob([buffer], { type: contentType || 'image/*' }));
            previewContent.dataset.blobUrl = blobUrl;
            previewContent.innerHTML = `<img src="${blobUrl}" alt="${escapeHtml(fileName)}" style="max-width: 100%; max-height: 400px; border-radius: 4px;">`;
            return;
        }

        // PDF: render via an <iframe> for reliable cross-browser display.
        if (contentType.includes('pdf') || header === '%PDF') {
            const pdfBlobUrl = URL.createObjectURL(new Blob([buffer], { type: 'application/pdf' }));
            previewContent.dataset.blobUrl = pdfBlobUrl;
            previewContent.innerHTML = `<div style="padding:12px; text-align:center;">
                <iframe src="${pdfBlobUrl}" title="${escapeHtml(fileName)}" style="width:100%; height:560px; border:1px solid #ddd; border-radius:6px; background:#fff;"></iframe>
                <div style="margin-top:10px; display:flex; justify-content:center; gap:12px; align-items:center; flex-wrap:wrap;">
                    <a href="${pdfBlobUrl}" target="_blank" rel="noopener" style="font-size:12px; color:#3498db;">Open in new tab ↗</a>
                    <a href="${API_BASE}/files/${id}/download" target="_blank" rel="noopener" style="font-size:12px; color:#3498db;">Download file</a>
                </div>
            </div>`;
            return;
        }

        // Text-like content
        if (contentType.startsWith('text/') || ['txt', 'json', 'xml', 'csv'].includes(ext) || header !== '%PDF' && buffer.byteLength < 200000) {
            const text = new TextDecoder().decode(buffer);
            previewContent.innerHTML = `<pre style="text-align: left; max-height: 400px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
            return;
        }

        // Markdown fallback (render as text if small)
        if (ext === 'md') {
            const text = new TextDecoder().decode(buffer);
            previewContent.innerHTML = `<pre style="text-align: left; max-height: 400px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
            return;
        }

        // Unknown binary: show download link
        previewContent.innerHTML = `<p style="color: #e74c3c;">${adminT('noPreviewAvailable', 'No preview available for this file type. Use download.')}</p>`;
    } catch (error) {
        previewContent.innerHTML = `<div style="padding: 12px; text-align: center;">
            <p style="color: #e74c3c; margin-bottom: 10px;">Failed to load file preview. The file may still be processing or the server encountered an error.</p>
            <div style="display:flex; justify-content:center; gap:12px; flex-wrap:wrap;">
                <a href="${API_BASE}/files/${id}/download" target="_blank" rel="noopener" style="font-size:13px; color:#3498db;">Download file instead</a>
                <button type="button" data-preview-id="${escapeHtml(String(id))}" data-preview-name="${escapeHtml(fileName || '')}" onclick="previewFile(this.dataset.previewId, this.dataset.previewName);" style="font-size:13px; padding:4px 10px; background:#0b63a6; color:white; border:none; border-radius:4px; cursor:pointer;">Retry</button>
            </div>
        </div>`;
    }
}



// Auto Groups are view-only - no form submission needed
// Removed form submission handler as Auto Groups display conversation history and files







// Handle file attachments for prompts
let promptPreviewFile = null;
let promptSelectedFiles = [];
 // Reminder file attachments (separate from prompts)
let currentConversationHistory = []; // Track conversation messages
// conversation mapping is now server-side; frontend will not persist ConversationId
let pendingRecentFileChoices = null;
let pendingRecentFileChoicesExpiresAt = 0;
const RECENT_FILE_SELECTION_WINDOW_MS = 180000;
window.promptSelectedFiles = promptSelectedFiles;

let lastAutoSavedSnapshot = null;
let autoSaveInFlight = false;
const draftStorageKey = 'adminPromptDraft';
// Toggle automatic draft saving/restoring in the browser (false = disabled)
// NOTE: enable to restore autosave for groups and drafts
const AUTO_SAVE_ENABLED = true;









function closeAutoGroupView() {
    document.querySelectorAll('.sidebar-prompt-item').forEach(item => item.classList.remove('active'));
    const promptIdInput = document.getElementById('prompt-id');
    if (promptIdInput) promptIdInput.value = '';
    const responseContent = document.getElementById('response-content');
    if (responseContent) {
        responseContent.innerHTML = '<span style="color: #2f3a3f;">Enter a prompt and click Send to get a response</span>';
    }
}

function updatePromptFilesList() {
    const filesList = document.getElementById('prompt-files-list');
    const promptFilesIcon = document.getElementById('prompt-files-icon');
    
    if (!filesList) return;
    
    // Update paperclip icon color
    if (promptFilesIcon) {
        if (promptSelectedFiles.length === 0) {
            promptFilesIcon.style.color = '#2f3a3f'; // Gray when no files
        } else {
            promptFilesIcon.style.color = '#0b63a6'; // Blue when files present
        }
    }
    
    if (promptSelectedFiles.length === 0) {
        filesList.innerHTML = `<div style="color: #2f3a3f; text-align: center; padding: 20px; font-size: 14px;">${adminT('noUploadsToShow', 'No uploads to show')}</div>`;
    } else {
        filesList.innerHTML = promptSelectedFiles.map((fileObj, index) => {
            const fileName = fileObj.file?.name || fileObj.name || 'Document';
            const fileId = fileObj.documentId || fileObj.id || '';
            const isPending = !!fileObj.pending;
            const progress = Number.isFinite(fileObj.uploadProgress) ? Math.max(0, Math.min(100, Math.round(fileObj.uploadProgress))) : null;
            const statusText = isPending ? (fileObj.statusText || 'Loading...') : (fileObj.statusText || '');
            const previewDisabled = isPending || !fileId;
            return `
                <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0; ${fileObj.selected ? 'background: #f0f0f0;' : ''} ${isPending ? 'opacity: 0.85;' : ''}">
                    <input type="checkbox" ${fileObj.selected ? 'checked' : ''} style="width: 16px; height: 16px; cursor: pointer; margin: 0;" onclick="togglePromptFileSelection(${index})">
                    <span onclick="togglePromptFileSelection(${index})" style="font-size: 14px; color: #333; flex: 1; overflow: hidden; text-overflow: ellipsis; cursor: pointer; user-select: none;">
                        <span style="display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${escapeHtml(fileName)}</span>
                        ${statusText ? `<small style="display: block; color: ${isPending ? '#e67e22' : '#7f8c8d'}; margin-top: 2px;">${escapeHtml(statusText)}${progress !== null && isPending ? ` (${progress}%)` : ''}</small>` : ''}
                    </span>
                    <button type="button" ${previewDisabled ? 'disabled' : ''} onclick="event.stopPropagation(); previewPromptSelectedFile(${index})" style="padding: 4px 10px; background: ${previewDisabled ? '#95a5a6' : '#0b63a6'}; color: white; border: none; border-radius: 4px; cursor: ${previewDisabled ? 'not-allowed' : 'pointer'}; font-size: 12px;">
                        ${isPending ? 'Loading...' : 'Preview'}
                    </button>
                </div>
            `;
        }).join('');
    }
}

window.togglePromptFileSelection = function(index) {
    if (!promptSelectedFiles[index]) return;
    promptSelectedFiles[index].selected = !promptSelectedFiles[index].selected;
    window.promptSelectedFiles = promptSelectedFiles;
    updatePromptFilesList();
};

// After a file is uploaded, the server generates a slug asynchronously.
// Poll until the server-assigned fileName differs from the original, then update the UI.
function scheduleFileNameRefresh(fileObj, documentId, originalName) {
    if (!documentId) return;
    let attempts = 0;
    const maxAttempts = 10;
    const intervalMs = 4000;
    const poll = async () => {
        attempts++;
        try {
            const res = await fetch(`/api/documents/${documentId}`);
            if (res.ok) {
                const meta = await res.json().catch(() => null);
                const serverName = (meta && (meta.fileName || meta.FileName)) || null;
                if (serverName && serverName !== originalName) {
                    fileObj.name = serverName;
                    window.promptSelectedFiles = promptSelectedFiles;
                    updatePromptFilesList();
                    // Refresh groups silently so the card counter and modal reflect the
                    // evicted duplicate ManagedFile (server removes it when doc is linked)
                    if (typeof loadGroups === 'function') {
                        Promise.resolve().catch(() => {});
                    }
                    return; // Name updated — stop polling
                }
            }
        } catch { /* noop */ }
        if (attempts < maxAttempts) {
            setTimeout(poll, intervalMs);
        }
    };
    setTimeout(poll, 4000); // Give the background task a head-start
}

window.deleteSelectedPromptFiles = function() {
    const selectedCount = promptSelectedFiles.filter(f => f.selected).length;
    if (selectedCount === 0) {
        showMessage('No files selected', 'info');
        return;
    }

    promptSelectedFiles = promptSelectedFiles.filter(f => !f.selected);
    syncPromptSelectedFilesState();
};










async function restoreDraftConversation() {
    try {
        // Fetch recent prompts from server and try to find the most recent prompt
        // whose PromptText looks like an autosave snapshot (messages/files JSON)
        const res = await fetchWithRetry(`${API_BASE}/prompts?page=1&pageSize=20`);
        if (!res.ok) return false;
        const json = await res.json().catch(() => null);
        const prompts = (json && (json.data || json.Data)) || [];

        let found = null;
        for (const p of prompts) {
            const text = p.PromptText || p.promptText || p.Prompttext || p.prompttext;
            if (!text) continue;
            try {
                const draft = JSON.parse(text);
                if (draft && (Array.isArray(draft.messages) || Array.isArray(draft.files))) {
                    found = { draft, id: p.Id || p.id };
                    break;
                }
            } catch { /* not JSON */ }
        }

        if (!found) return false;

        const draft = found.draft;
        currentConversationHistory = Array.isArray(draft.messages) ? draft.messages : [];
        promptSelectedFiles = Array.isArray(draft.files) ? draft.files.map(f => ({
            name: f.name || 'Document',
            documentId: f.id || f.documentId,
            selected: false
        })) : [];
        window.promptSelectedFiles = promptSelectedFiles;
        updatePromptFilesList();

        const promptIdInput = document.getElementById('prompt-id');
        if (promptIdInput && found.id) promptIdInput.value = found.id;

        if (currentConversationHistory.length > 0) {
            const conversationHTML = currentConversationHistory.map(msg => {
                const msgFiles = Array.isArray(msg.files) ? msg.files : [];
                const filesData = encodeFilesData(msgFiles);
                const filesTitle = msgFiles.length > 0
                    ? `${msgFiles.length} file(s) attached: ${msgFiles.map(f => (typeof f === 'string' ? f : f.name || f.fileName || f.FileName || 'Document')).join(', ')}`
                    : '';
                const msgSources = Array.isArray(msg.sources) ? msg.sources : [];
                const msgSourcesHtml = msgSources.length > 0 ? `
                    <div style="margin-top: 8px; padding: 8px; background: #fff; border: 1px solid #dee2e6; border-radius: 4px;">
                        <div style="font-weight: 600; color: #2c3e50; margin-bottom: 6px; font-size: 0.8rem;">Sources Used:</div>
                        ${msgSources.map(source => {
                            const documentId = source.documentId || source.DocumentId;
                            const fileName = source.fileName || source.FileName || 'Document';
                            const page = source.pageNumber || source.PageNumber;
                            const label = page ? `${fileName} (p.${page})` : fileName;
                            const href = documentId ? `/api/documents/${documentId}/download` : '#';
                            const linkStyle = documentId ? 'color: #0b63a6; text-decoration: underline;' : 'color: #2f3a3f; text-decoration: none;';
                            return `
                                <div style="display: flex; align-items: center; gap: 8px; font-size: 12px;">
                                    <a href="${href}" target="_blank" style="${linkStyle}" onclick="${documentId ? '' : 'return false;'}">${escapeHtml(label)}</a>
                                    ${documentId ? `<button type=\"button\" onclick=\"previewDocumentFile('${documentId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')\" style=\"padding: 2px 6px; background: #0b63a6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 11px;\">Preview</button>` : ''}
                                </div>
                            `;
                        }).join('')}
                    </div>
                ` : '';

                return `
                    <div style="margin-bottom: 12px;">
                        <div style="color: ${msg.role === 'user' ? '#0b63a6' : '#27ae60'}; font-weight: 600; margin-bottom: 4px; display: flex; align-items: center; gap: 8px;">
                            <span>${msg.role === 'user' ? 'User' : 'Bot'}:</span>
                            ${msgFiles.length > 0 ? `<button type=\"button\" onclick=\"showAttachedFilesModal('${filesData}')\" style=\"background: none; border: none; font-size: 16px; color: #0b63a6; cursor: pointer; padding: 0;\" title=\"${escapeHtml(filesTitle)}\">📎</button>` : ''}
                        </div>
                        <div style="color: #444; padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
                        ${msg.role === 'bot' ? msgSourcesHtml : ''}
                    </div>
`;
            }).join('');

            const responseContent = document.getElementById('response-content');
            if (responseContent) responseContent.innerHTML = conversationHTML;
        }

        return true;
    } catch (error) {
        console.warn('[AUTO SAVE] Failed to restore draft from server:', error);
        return false;
    }
}

async function deletePrompt(id) {
    if (!confirm('Are you sure you want to delete this prompt?')) return;

    try {
        const response = await fetch(`${API_BASE}/prompts/${id}`, { method: 'DELETE' });
        if (response.ok || response.status === 204) {
            showMessage('Prompt deleted successfully');
            await loadPrompts(true);

        } else {
            const error = await response.json().catch(() => null);
            showMessage((error && (error.detail || error.error)) || 'Error deleting prompt', 'error');
        }
    } catch (error) {
        showMessage('Error deleting prompt: ' + (error?.message || error), 'error');
    }
}

async function deleteConversation(id, title) {
    if (!id) return;
    const name = title || 'this conversation';
    if (!confirm(`Delete ${name}? This will permanently remove its messages.`)) return;

    try {
        const response = await fetch(`/api/conversations/${id}`, { method: 'DELETE' });
        if (response.ok || response.status === 204) {
            if (activeConversationId && String(activeConversationId) === String(id)) {

                closeAutoGroupView();
            }
            removeConversationFromUI(id);
            showMessage('Conversation deleted successfully');
            apiCache.invalidate('/api/conversations');
            apiCache.invalidate('/api/admin/groups');
            await loadPrompts(true);
            await Promise.resolve();

        } else {
            const error = await response.json().catch(() => null);
            showMessage((error && (error.detail || error.error)) || 'Error deleting conversation', 'error');
        }
    } catch (error) {
        showMessage('Error deleting conversation: ' + (error?.message || error), 'error');
    }
}

function removeConversationFromUI(id) {
    if (!id) return;
    try {
        const item = document.querySelector(`.sidebar-prompt-item[data-conversation-id="${id}"]`);
        if (item) item.remove();
    } catch (e) {
        console.warn('Failed to remove conversation item from list', e);
    }

    try {
        const modalRow = document.querySelector(`[data-modal-conversation-id="${id}"]`);
        if (modalRow) modalRow.remove();
    } catch (e) {
        console.warn('Failed to remove conversation item from modal', e);
    }

    try {
        if (Array.isArray(state.prompts.data)) {
            state.prompts.data = state.prompts.data.filter(c => String(getProp(c, 'id') || getProp(c, 'Id')) !== String(id));
        }
    } catch (e) {
        console.warn('Failed to update prompts state after delete', e);
    }
}

async function updateConversationTitle(id, newTitle, originalTitle) {
    if (!id || !newTitle) return false;
    try {
        const response = await fetch(`/api/conversations/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: newTitle })
        });
        if (response.ok) {
            showMessage('Conversation renamed');
            apiCache.invalidate('/api/conversations');
            apiCache.invalidate('/api/admin/groups');
            return true;
        }
        const error = await response.json().catch(() => null);
        showMessage((error && (error.detail || error.error)) || 'Error renaming conversation', 'error');
        return false;
    } catch (error) {
        showMessage('Error renaming conversation: ' + (error?.message || error), 'error');
        return false;
    }
}

// Prompt response tracking functions
function showPromptResponse(promptName, promptText) {
    const responseContent = document.getElementById('response-content');
    
    responseContent.innerHTML = `
        <div style="margin-bottom: 10px; color: #666;">
            <strong>Selected:</strong> ${escapeHtml(promptName)}
        </div>
        <div style="padding: 10px; background: #f8f9fa; border-radius: 4px; font-size: 0.9rem; color: #555;">
            ${escapeHtml(promptText.substring(0, 200))}${promptText.length > 200 ? '...' : ''}
        </div>
        <div style="margin-top: 10px; color: #2f3a3f; font-size: 0.85rem;">
            Click "Track" to monitor this prompt's usage
        </div>
    `;
}

function hidePromptResponse() {
    const responseContent = document.getElementById('response-content');
    responseContent.innerHTML = '<span style="color: #2f3a3f;">Click Track to monitor prompt responses</span>';
}

function trackPrompt() {
    const responseContent = document.getElementById('response-content');
    const selectedItem = document.querySelector('.sidebar-prompt-item.active');
    
    if (!selectedItem) {
        showMessage('Please select a prompt first', 'error');
        return;
    }
    
    responseContent.innerHTML = `
        <div style="text-align: center; padding: 20px;">
            <div style="color: #0b63a6; font-size: 1.2rem; margin-bottom: 10px;">📊 Tracking Active</div>
            <div style="color: #666; font-size: 0.9rem;">Monitoring prompt usage and responses...</div>
            <div style="margin-top: 15px; padding: 10px; background: #e8f5e9; border-radius: 4px; color: #2e7d32;">
                ✓ Ready to track interactions
            </div>
        </div>
    `;
    
    showMessage('Tracking enabled for selected prompt');
}

// Make functions globally available
window.trackPrompt = trackPrompt;
window.viewConversation = async function(conversationId, title) {
    try {
        // Close the modal
        const modal = document.getElementById('manage-group-modal');
        if (modal) modal.classList.remove('active');
        
        // Switch to the prompts tab if not already there
        const promptsTab = document.querySelector('.tab[data-tab="prompts"]');
        if (promptsTab) {
            document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
            document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
            promptsTab.classList.add('active');
            document.getElementById('prompts').classList.add('active');
        }
        
        // Load the conversation into view
        activeConversationId = conversationId;
        await loadConversationIntoView(conversationId, title);
        showMessage(`Loaded conversation: ${title}`, 'success');
    } catch (error) {
        console.error('[VIEW CONVERSATION] Error:', error);
        showMessage('Failed to load conversation: ' + error.message, 'error');
    }
};

// Groups with lazy loading








let isEditingGroup = false;

document.getElementById('group-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const id = document.getElementById('group-id').value;
    const name = document.getElementById('group-name').value;
    const description = document.getElementById('group-description').value;
    
    const data = { name, description };
    
    try {
        const url = isEditingGroup ? `${API_BASE}/groups/${id}` : `${API_BASE}/groups`;
        const method = isEditingGroup ? 'PUT' : 'POST';
        
        const response = await fetch(url, {
            method,
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        
        if (response.ok) {
            const result = await response.json();
            const groupId = result.id || result.Id;
            
            console.log('[CREATE GROUP] Group created with ID:', groupId);
            console.log('[CREATE GROUP] Files to upload:', window.groupSelectedFiles);
            
            // Upload files if any are selected
            if (window.groupSelectedFiles && window.groupSelectedFiles.length > 0) {
                console.log('[CREATE GROUP] Uploading', window.groupSelectedFiles.length, 'files...');
                
                    for (const fileObj of window.groupSelectedFiles) {
                    const formData = new FormData();
                    formData.append('file', fileObj.file);
                    
                    try {
                        console.log('[CREATE GROUP] Uploading file:', fileObj.name);
                        formData.append('label', fileObj.name || fileObj.file?.name || 'Document');
                        const uploadRes = await fetch(`${API_BASE}/files`, {
                            method: 'POST',
                            body: formData
                        });
                        
                        console.log('[CREATE GROUP] Upload response status:', uploadRes.status);
                        
                        if (uploadRes.ok) {
                            const fileData = await uploadRes.json();
                            const fileId = fileData.id || fileData.Id;
                            console.log('[CREATE GROUP] File uploaded with ID:', fileId);
                            
                            // Add file to group
                            const attachRes = await fetch(`${API_BASE}/groups/${groupId}/files/${fileId}`, {
                                method: 'POST'
                            });
                            console.log('[CREATE GROUP] Attach file response status:', attachRes.status);
                            
                            if (attachRes.ok) {
                                console.log('[CREATE GROUP] File successfully attached to group');
                            } else {
                                console.error('[CREATE GROUP] Failed to attach file to group');
                            }
                        } else {
                            const errorText = await uploadRes.text();
                            console.error('[CREATE GROUP] Failed to upload file:', errorText);
                        }
                    } catch (uploadError) {
                        console.error('[CREATE GROUP] Error uploading file:', uploadError);
                    }
                }
                
                console.log('[CREATE GROUP] All files processed, clearing local list');
                // Clear files after successful upload - need to use splice to modify the actual array
                groupSelectedFiles.length = 0; // Clear the array in place

                if (typeof window.updateGroupFilesList === 'function') {
                    window.updateGroupFilesList();
                }
                const groupFileSection = document.getElementById('group-file-section');
                if (groupFileSection) {
                    groupFileSection.style.display = 'none';
                }
            } else {
                console.log('[CREATE GROUP] No files to upload');
            }
            
            showMessage(isEditingGroup ? 'Reminder updated successfully' : 'Reminder created successfully');
            cancelEditGroup();
            await Promise.resolve();
             // Force refresh groups cache
        } else {
            const error = await response.json().catch(() => null);
            showMessage(error?.detail || 'Error saving reminder', 'error');
        }
    } catch (error) {
        showMessage('Error saving reminder: ' + error.message, 'error');
    }
});

// Reminder form submit handler
document.getElementById('reminder-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const id = document.getElementById('reminder-id').value;
    const name = document.getElementById('reminder-name').value;
    const description = 'Reminder'; // Default description for reminders
    
    const data = { name, description };
    
    try {
        // Reminders use the same groups API but we distinguish them by description
        const url = id ? `${API_BASE}/groups/${id}` : `${API_BASE}/groups`;
        const method = id ? 'PUT' : 'POST';
        
        const response = await fetch(url, {
            method,
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        
        if (response.ok) {
            const result = await response.json();
            const groupId = result.id || result.Id;
            
            console.log('[CREATE REMINDER] Reminder created with ID:', groupId);
            console.log('[CREATE REMINDER] Active prompt group ID:', activePromptGroupId);
            console.log('[CREATE REMINDER] Files to upload:', window.reminderSelectedFiles);
            
            // Auto-group: If there's an active prompt group, associate the reminder with it
            if (activePromptGroupId && !id) {
                try {
                    console.log('[CREATE REMINDER] Linking reminder to active group:', activePromptGroupId);
                    // Create a conversation for this reminder and link it to the active group
                    const conversationData = {
                        name: name,
                        resourceGroupId: activePromptGroupId
                    };
                    const convRes = await fetch(`/api/conversations`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(conversationData)
                    });
                    if (convRes.ok) {
                        const convResult = await convRes.json();
                        const conversationId = convResult.id || convResult.Id;
                        console.log('[CREATE REMINDER] Created conversation:', conversationId, 'for reminder in group');
                        
                        // Link the conversation to the reminder (group)
                        const linkRes = await fetch(`${API_BASE}/groups/${groupId}/prompts/${conversationId}`, {
                            method: 'POST'
                        });
                        if (linkRes.ok || linkRes.status === 204) {
                            console.log('[CREATE REMINDER] Successfully linked reminder to active group');
                        } else {
                            console.warn('[CREATE REMINDER] Failed to link reminder to conversation');
                        }
                    } else {
                        console.warn('[CREATE REMINDER] Failed to create conversation for auto-grouping');
                    }
                } catch (autoGroupError) {
                    console.error('[CREATE REMINDER] Error during auto-grouping:', autoGroupError);
                }
            }
            
            // Upload files if any are selected
            if (window.reminderSelectedFiles && window.reminderSelectedFiles.length > 0) {
                console.log('[CREATE REMINDER] Uploading', window.reminderSelectedFiles.length, 'files...');
                
                    for (const fileObj of window.reminderSelectedFiles) {
                    const formData = new FormData();
                    formData.append('file', fileObj.file);
                    
                    try {
                        console.log('[CREATE REMINDER] Uploading file:', fileObj.name);
                        formData.append('label', fileObj.name || fileObj.file?.name || 'Document');
                        const uploadRes = await fetch(`${API_BASE}/files`, {
                            method: 'POST',
                            body: formData
                        });
                        
                        if (uploadRes.ok) {
                            const fileData = await uploadRes.json();
                            const fileId = fileData.id || fileData.Id;
                            console.log('[CREATE REMINDER] File uploaded with ID:', fileId);
                            
                            // Add file to reminder/group
                            const attachRes = await fetch(`${API_BASE}/groups/${groupId}/files/${fileId}`, {
                                method: 'POST'
                            });
                            
                            if (attachRes.ok) {
                                console.log('[CREATE REMINDER] File successfully attached to reminder');
                            } else {
                                console.error('[CREATE REMINDER] Failed to attach file to reminder');
                            }
                        } else {
                            const errorText = await uploadRes.text();
                            console.error('[CREATE REMINDER] Failed to upload file:', errorText);
                        }
                    } catch (uploadError) {
                        console.error('[CREATE REMINDER] Error uploading file:', uploadError);
                    }
                }
                
                console.log('[CREATE REMINDER] All files processed, clearing local list');
                reminderSelectedFiles.length = 0;
                if (typeof window.updateReminderFilesList === 'function') {
                    window.updateReminderFilesList();
                }
                const reminderFileSection = document.getElementById('reminder-file-section');
                if (reminderFileSection) {
                    reminderFileSection.style.display = 'none';
                }
                const reminderPreviewPanel = document.getElementById('reminder-preview-panel');
                if (reminderPreviewPanel) {
                    reminderPreviewPanel.style.display = 'block';
                }
                const reminderPreviewContent = document.getElementById('reminder-preview-content');
                if (reminderPreviewContent) {
                    reminderPreviewContent.innerHTML = '<div style="font-size: 48px; color: #95a5a6; margin-bottom: 10px;">📄</div>';
                }
                const reminderPreviewFilename = document.getElementById('reminder-preview-filename');
                if (reminderPreviewFilename) {
                    reminderPreviewFilename.textContent = '';
                }
                const reminderFileInput = document.getElementById('reminder-file-input');
                if (reminderFileInput) {
                    reminderFileInput.value = '';
                }
            } else {
                console.log('[CREATE REMINDER] No files to upload');
            }
            
            showMessage(id ? 'Reminder updated successfully' : 'Reminder created successfully');
            document.getElementById('reminder-form').reset();
            document.getElementById('reminder-id').value = '';
            apiCache.invalidate('/api/admin/groups');
            await loadReminders(true);
        } else {
            const error = await response.json().catch(() => null);
            showMessage(error?.detail || 'Error saving reminder', 'error');
        }
    } catch (error) {
        showMessage('Error saving reminder: ' + error.message, 'error');
    }
});

// Enter key handler for group name input
document.getElementById('group-name')?.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') {
        e.preventDefault();
        const groupForm = document.getElementById('group-form');
        if (groupForm) {
            groupForm.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
        }
    }
});

// Enter key handler for reminder name input
document.getElementById('reminder-name')?.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') {
        e.preventDefault();
        const reminderForm = document.getElementById('reminder-form');
        if (reminderForm) {
            reminderForm.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
        }
    }
});





document.getElementById('group-cancel-btn')?.addEventListener('click', cancelEditGroup);



async function loadPromptById(promptId, promptName) {
    if (!promptId) return;
    try {
        let prompt = null;
        const directRes = await fetchWithRetry(`${API_BASE}/prompts/${promptId}`);
        if (directRes.ok) {
            const directJson = await directRes.json().catch(() => null);
            prompt = (directJson && (directJson.data || directJson)) || directJson || null;
        }

        if (!prompt) {
            const listRes = await fetchWithRetry(`${API_BASE}/prompts?page=1&pageSize=100`);
            if (listRes.ok) {
                const listJson = await listRes.json().catch(() => null);
                const prompts = (listJson && (listJson.data || listJson.Data)) || listJson || [];
                prompt = prompts.find(p => String(getProp(p, 'id')) === String(promptId)) || null;
            }
        }

        if (!prompt) {
            showMessage('Unable to load prompt details', 'error');
            return;
        }

        const promptText = getProp(prompt, 'promptText') || '';
        loadPromptIntoForm(promptId, promptName || getProp(prompt, 'name') || 'Prompt', promptText);
        showMessage('Prompt loaded from group', 'success');
    } catch (error) {
        showMessage('Error loading prompt: ' + error.message, 'error');
    }
}

// Manage Group Resources (existing modal functionality)


// Quick add to group functionality










// Preview file from reminder modal


async function resolveDocumentIdByName(fileName) {
    if (!fileName) return null;
    try {
        const response = await fetch('/api/documents?page=1&pageSize=100');
        if (!response.ok) return null;
        const data = await response.json();
        const docs = data.documents || data.Documents || [];
        const match = docs.find(doc => (doc.fileName || doc.FileName || '').toLowerCase() === fileName.toLowerCase());
        return match ? (match.id || match.Id) : null;
    } catch {
        return null;
    }
}

// Preview a document stored in /api/documents
async function previewDocumentFile(documentId, fileName) {
    let resolvedId = documentId;
    if (!resolvedId && fileName) {
        resolvedId = await resolveDocumentIdByName(fileName);
    }

    if (!resolvedId) {
        showMessage('Unable to locate document for preview', 'error');
        return;
    }

    const modal = document.getElementById('reminder-file-preview-modal');
    const modalContent = document.getElementById('reminder-file-preview-content');
    const modalTitle = document.getElementById('reminder-file-preview-title');
    const safeName = fileName || 'Document';

    modalTitle.textContent = safeName;
    modalContent.innerHTML = `<p style="color: #95a5a6; text-align: center; padding: 20px;">${adminT('loadingPreview', 'Loading preview...')}</p>`;
    modal.style.display = 'flex';

    const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
    const previewUrl = `/api/documents/${resolvedId}/download?v=${cacheBuster}`;
    const ext = (safeName.split('.').pop() || '').toLowerCase();

    if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg'].includes(ext)) {
        const img = new Image();
        img.onload = function() {
            modalContent.innerHTML = `<img src="${previewUrl}" alt="${escapeHtml(safeName)}" style="max-width: 100%; max-height: 55vh; width: auto; height: auto; border-radius: 4px; object-fit: contain; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">`;
        };
        img.onerror = function() {
            modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load image.</p>';
        };
        img.src = previewUrl;
    } else if (ext === 'pdf') {
        try {
            const response = await fetch(previewUrl);
            if (!response.ok) throw new Error('Failed to load PDF');
            const buffer = await response.arrayBuffer();
            const header = new TextDecoder().decode(new Uint8Array(buffer.slice(0, 4)));
            if (header !== '%PDF') {
                const text = new TextDecoder().decode(buffer);
                modalContent.innerHTML = `<pre style="text-align: left; max-height: 55vh; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
            } else {
                const blobUrl = URL.createObjectURL(new Blob([buffer], { type: 'application/pdf' }));
                modalContent.dataset.blobUrl = blobUrl;
                modalContent.innerHTML = `<div style="text-align:center;"><iframe src="${blobUrl}" title="${escapeHtml(safeName || fileName || 'Document')}" style="width:100%; height:55vh; min-height:400px; border:none; border-radius:4px; background:#fff;"></iframe><div style="margin-top:8px;"><a href="${blobUrl}" target="_blank" rel="noopener" style="font-size:12px; color:#3498db;">Open in new tab ↗</a></div></div>`;
            }
        } catch (error) {
            modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load file.</p>';
        }
    } else if (['txt', 'json', 'xml', 'csv'].includes(ext)) {
        try {
            const response = await fetch(previewUrl);
            if (response.ok) {
                const text = await response.text();
                modalContent.innerHTML = `<pre style="text-align: left; max-height: 55vh; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
            } else {
                modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load file.</p>';
            }
        } catch (error) {
            modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load file.</p>';
        }
    } else if (ext === 'md') {
        try {
            const response = await fetch(previewUrl);
            if (response.ok) {
                const text = await response.text();
                const renderedHtml = renderMarkdown(text);
                modalContent.innerHTML = `<div style="text-align: left; max-height: 55vh; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 14px;">${renderedHtml}</div>`;
            } else {
                modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load file.</p>';
            }
        } catch (error) {
            modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load file.</p>';
        }
    } else {
        modalContent.innerHTML = `
            <div style="text-align: center; padding: 40px;">
                <div style="font-size: 64px; margin-bottom: 20px;">📄</div>
                <p style="color: #2f3a3f; font-size: 14px;">Preview not available for this file type.</p>
                <p style="color: #95a5a6; font-size: 12px; margin-top: 10px;">File: ${escapeHtml(safeName)}</p>
            </div>
        `;
    }
}

function showAttachedFilesModal(encodedFiles) {
    let files = [];
    try {
        files = JSON.parse(decodeURIComponent(encodedFiles));
    } catch (error) {
        showMessage('Unable to read attached files', 'error');
        return;
    }

    if (!Array.isArray(files) || files.length === 0) {
        showMessage('No attached files to preview', 'error');
        return;
    }

    const modalBody = document.getElementById('modal-content-body');
    modalBody.innerHTML = `
        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;">
            <h3 style="margin: 0; font-size: 16px; color: #2c3e50;">Attached Files</h3>
            <button type="button" onclick="closeManageGroupModal()" style="background: none; border: none; font-size: 20px; color: #95a5a6; cursor: pointer; padding: 0; line-height: 1;">×</button>
        </div>
        <div style="border: 1px solid #e0e0e0; border-radius: 6px; background: #fff; max-height: 320px; overflow-y: auto;">
            ${files.map(file => {
                const fileId = file.id || file.documentId || '';
                const fileName = file.name || 'Document';
                return `
                    <div style="padding: 10px 12px; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid #f0f0f0; gap: 10px;">
                        <span style="font-size: 14px; color: #333; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${escapeHtml(fileName)}</span>
                        <button type="button" onclick="previewDocumentFile('${fileId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')" style="padding: 4px 10px; background: #0b63a6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;">
                            Preview
                        </button>
                    </div>
                `;
            }).join('')}
        </div>
    `;

    document.getElementById('manage-group-modal').classList.add('active');
}



// Utility functions
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatFileSize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
    if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
    return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
}

// Close modal event listener
document.getElementById('close-modal-btn')?.addEventListener('click', closeManageGroupModal);

// Close modal when clicking outside the modal content (overlay click)
function attachOverlayClose(modalId, closeFn) {
    try {
        const modal = document.getElementById(modalId);
        if (!modal) return;
        modal.addEventListener('click', function (ev) {
            if (ev.target === modal) {
                try { closeFn(); } catch (e) { modal.classList.remove('active'); modal.style.display = 'none'; }
            }
        });
    } catch (err) {
        console.warn('attachOverlayClose failed for', modalId, err);
    }
}

attachOverlayClose('manage-group-modal', closeManageGroupModal);
attachOverlayClose('group-file-preview-modal', function() {
    const m = document.getElementById('group-file-preview-modal'); if (m) m.style.display = 'none';
});
attachOverlayClose('reminder-file-preview-modal', closeReminderFilePreviewModal);

// Infinite scroll implementation
function setupInfiniteScroll() {
    const containers = [
        { id: 'files-list', loadFn: loadFiles },
        { id: 'prompts-list', loadFn: loadPrompts },
        { id: 'reminders-list', loadFn: loadReminders },
        { id: 'groups-list', loadFn: loadGroups }
    ];
    
    containers.forEach(({ id, loadFn }) => {
        const container = document.getElementById(id);
        if (!container) return; // element may not exist in the current admin view
        
        // Create sentinel element at the bottom of each list
        const sentinel = document.createElement('div');
        sentinel.className = 'scroll-sentinel';
        sentinel.style.height = '10px';
        container.appendChild(sentinel);
        
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    loadFn();
                }
            });
        }, { threshold: 0.1 });
        
        observer.observe(sentinel);
    });
}

// Wait for API to be ready (up to 3 minutes to survive ECS cold-start)
async function waitForAPI(maxRetries = 60, delayMs = 3000) {
    // Match the home-page WakeupGate behavior by signaling wake immediately.
    triggerAdminWake();

    for (let i = 0; i < maxRetries; i++) {
        try {
            const elapsed = Math.round((i * delayMs) / 1000);
            console.log(`[API Health] Checking API health (attempt ${i + 1}/${maxRetries}, ${elapsed}s elapsed)...`);
            // Poll /health instead of /ready: /health has a dedicated CloudFront behaviour
            // that routes to the ALB and returns 503 when ECS is at 0 tasks (no healthy
            // targets).  /ready has no such behaviour and falls through to the S3 default
            // origin, which returns a 403 that CloudFront converts to a 200 with index.html
            // — causing waitForAPI to return true immediately even when the server is still
            // sleeping.  /health always returns 200 as soon as the ECS process is running,
            // which is all we need to unblock the admin panel (individual API calls handle
            // their own DB-readiness retries).
            const response = await fetch('/health', { cache: 'no-store' });
            if (response.ok) {
                console.log('[API Health] API is ready!');
                return true;
            }

            // If not ready yet, keep retrying wake in case the initial wake call was dropped.
            triggerAdminWake();
            console.warn(`[API Health] API returned ${response.status}, retrying...`);
        } catch (error) {
            triggerAdminWake();
            console.warn(`[API Health] API not ready yet:`, error.message);
        }

        if (i < maxRetries - 1) {
            const elapsed = Math.round(((i + 1) * delayMs) / 1000);
            const remaining = Math.round(((maxRetries - i - 1) * delayMs) / 1000);
            showMessage(`Server is starting up... (${elapsed}s elapsed, up to ${remaining}s more)`, 'info');
            await new Promise(resolve => setTimeout(resolve, delayMs));
        }
    }

    console.error('[API Health] API failed to become ready after', maxRetries, 'attempts');
    return false;
}

// Initial load
async function init() {
    console.log('[INIT] Starting admin panel initialization...');
    console.log('[INIT] Current time:', new Date().toISOString());
    console.log('[INIT] API Base:', API_BASE);

    // Keep the backend warm while the admin page is open, independent of the toggle.
    startAdminSessionKeepAlive();
    
    // Apply theme early so UI controls are available even if API health checks are slow
    try {
        const savedTheme = 'light';
        applyTheme(savedTheme);
        document.getElementById('theme-toggle')?.addEventListener('click', () => {
            const nextTheme = document.body.classList.contains('dark') ? 'light' : 'dark';
            applyTheme(nextTheme);
        });
    } catch (e) {
        console.warn('[INIT] Failed to attach theme toggle early:', e);
    }

    // Wait for API to be ready
    const apiReady = await waitForAPI();
    if (!apiReady) {
        showMessage('ERROR: The backend API is not responding. Please refresh the page.', 'error');
        return;
    }
    
    try {
        // Other initialization assumes API is available

        // Setup preview modal event listeners
        document.getElementById('close-preview')?.addEventListener('click', () => {
            document.getElementById('file-preview').style.display = 'none';
        });
        document.getElementById('download-file')?.addEventListener('click', () => {
            if (currentPreviewFile.id) {
                const downloadUrl = `${API_BASE}/files/${currentPreviewFile.id}/download`;
                window.location.href = downloadUrl;
            } else {
                showMessage('No file selected for download', 'error');
            }
        });

        // Start HTTP polling (replaces SignalR hubs)
        startHealthPolling();
        startResourcePolling();

        // Wire up Groq Live Clear button
        document.getElementById('groq-live-clear')?.addEventListener('click', async () => {
            try {
                await fetch(`${API_BASE}/groq-transcriptions`, { method: 'DELETE' });
                groqLiveEntries = [];
                groqLiveLastCount = 0;
                const list = document.getElementById('groq-live-list');
                const empty = document.getElementById('groq-live-empty');
                if (list) [...list.children].forEach(c => { if (c !== empty) c.remove(); });
                if (empty) empty.style.display = '';
            } catch (err) {
                console.warn('[GroqLive] Clear failed:', err);
            }
        });
        
        // Setup prompts file management
        const promptFileInput = document.getElementById('prompt-file-input');
        const promptFilesList = document.getElementById('prompt-files-list');
        const promptFilesIcon = document.getElementById('prompt-files-icon');
        const promptFileSection = document.getElementById('prompt-file-section');
        // Observe style/class changes to resist transient re-hides (kept in production as a small mitigation).
        try {
                if (promptFileSection) {
                    if (ADMIN_DEBUG) console.log('promptFileSection initial computed display:', window.getComputedStyle(promptFileSection).display);
                    // Track last explicit show time so we can resist transient hides from other inits
                    let _lastPromptShowTs = 0;
                    const _promptObserver = new MutationObserver((mutations) => {
                        for (const m of mutations) {
                            if (m.type === 'attributes') {
                                const computed = window.getComputedStyle(promptFileSection).display;
                                if (ADMIN_DEBUG) console.log('promptFileSection attribute change:', m.attributeName, 'style=', promptFileSection.style.cssText, 'computed=', computed);
                                // If the section was just shown and something immediately hid it, re-show briefly
                                try {
                                    const now = Date.now();
                                    if (computed === 'none' && _lastPromptShowTs && (now - _lastPromptShowTs) < 1000) {
                                        if (ADMIN_DEBUG) console.log('promptFileSection was re-hidden soon after show; forcing visible');
                                        promptFileSection.style.setProperty('display', 'block', 'important');
                                        promptFileSection.style.setProperty('visibility', 'visible', 'important');
                                        promptFileSection.style.setProperty('opacity', '1', 'important');
                                        const previewPanel = document.getElementById('prompt-preview-panel');
                                        const filesPanel = document.getElementById('prompt-files-panel');
                                        if (previewPanel) previewPanel.style.setProperty('display', 'block', 'important');
                                        if (filesPanel) filesPanel.style.setProperty('display', 'flex', 'important');
                                    }
                                } catch (e) { /* ignore */ }
                            }
                        }
                    });
                    _promptObserver.observe(promptFileSection, { attributes: true, attributeFilter: ['style', 'class'] });

                    // Expose helper to update last-show timestamp from other handlers
                    promptFileSection._markShown = () => { _lastPromptShowTs = Date.now(); };
                }
        } catch (e) {
            if (ADMIN_DEBUG) console.warn('Failed to attach MutationObserver to promptFileSection', e);
        }
        const promptPreviewContent = document.getElementById('prompt-preview-content');
        const promptPreviewFilename = document.getElementById('prompt-preview-filename');
        const promptUploadProgress = document.getElementById('prompt-upload-progress');
        const promptUploadBtn = document.getElementById('prompt-upload-btn');
        const promptFileInputElement = document.getElementById('prompt-file-input');
        const promptShowFileBtn = document.getElementById('prompt-show-file-btn');
        let promptUploadInFlight = false;

        const setPromptUploadLoading = (isLoading) => {
            if (promptUploadProgress) {
                promptUploadProgress.style.display = isLoading ? 'block' : 'none';
            }
            if (promptUploadBtn) {
                promptUploadBtn.disabled = isLoading;
                promptUploadBtn.style.opacity = isLoading ? '0.7' : '1';
                promptUploadBtn.textContent = isLoading ? 'Uploading...' : 'Upload';
            }
            if (promptFileInputElement) {
                promptFileInputElement.disabled = isLoading;
            }
            if (promptShowFileBtn) {
                promptShowFileBtn.disabled = isLoading;
                promptShowFileBtn.style.opacity = isLoading ? '0.7' : '1';
                promptShowFileBtn.style.cursor = isLoading ? 'not-allowed' : 'pointer';
            }
            if (promptFilesIcon) {
                promptFilesIcon.style.pointerEvents = isLoading ? 'none' : 'auto';
                promptFilesIcon.style.opacity = isLoading ? '0.5' : '1';
            }
        };
        
        
        // Setup reminders file management

         // Expose to global scope

        const groupFileInput = document.getElementById('group-file-input');
        const groupFilesList = document.getElementById('group-files-list');
        const groupFilesIcon = document.getElementById('group-files-icon');
        const groupFileSection = document.getElementById('group-file-section');
        const groupFilesDropdown = document.getElementById('group-files-dropdown');
        const groupFilesListDropdown = document.getElementById('group-files-list-dropdown');
        const groupPreviewContent = document.getElementById('group-preview-content');
        const groupPreviewFilename = document.getElementById('group-preview-filename');
        
        // Initialize updateGroupFilesList function first

        window.updateGroupFilesList = updateGroupFilesList;
        
        // Initialize files list display (after elements are defined)
        updateGroupFilesList();
        
        // Show file section when clicking "add file+" button
        document.getElementById('group-show-file-btn')?.addEventListener('click', () => {
            const isCurrentlyHidden = groupFileSection.style.display === 'none' || !groupFileSection.style.display;
            if (isCurrentlyHidden) {
                groupFileSection.style.display = 'block';
                document.getElementById('group-preview-panel').style.display = 'block';
                document.getElementById('group-files-panel').style.display = 'flex';
            } else {
                groupFileSection.style.display = 'none';
            }
        });
        
        // Show only Files panel when clicking paperclip in Reminders
        groupFilesIcon?.addEventListener('click', () => {
            const filesPanel = document.getElementById('group-files-panel');
            const deleteBtn = document.getElementById('group-delete-selected-btn');
            
            // Hide preview panel, show only files panel
            document.getElementById('group-preview-panel').style.display = 'none';
            filesPanel.style.display = 'flex';
            filesPanel.setAttribute('data-context', 'reminders');
            
            // Show delete button when opened from Reminders
            if (deleteBtn) {
                deleteBtn.style.display = 'block';
            }
            
            groupFileSection.style.display = 'block';
            
            // Update files list after showing the panel
            updateGroupFilesList();
        });
        
        // Handle file selection - show preview and auto-upload
        if (groupFileInput) {
            groupFileInput.addEventListener('change', (e) => {
                const file = e.target.files[0];
                if (file) {
                    groupPreviewFile = file;
                    groupPreviewFilename.textContent = file.name;
                    
                    // Show actual preview
                    const ext = file.name.split('.').pop().toLowerCase();
                    if (['jpg', 'jpeg', 'png', 'gif'].includes(ext)) {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            groupPreviewContent.innerHTML = `<img src="${e.target.result}" style="max-width: 100%; max-height: 200px; border-radius: 4px; object-fit: contain;">`;
                        };
                        reader.readAsDataURL(file);
                    } else if (ext === 'pdf') {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            groupPreviewContent.innerHTML = `<iframe src="${e.target.result}" title="PDF preview" style="width:100%; height:200px; border:none; border-radius:4px; background:#fff;"></iframe>`;
                        };
                        reader.readAsDataURL(file);
                    } else if (ext === 'md') {
                        // Render markdown files
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            const text = e.target.result;
                            const renderedHtml = renderMarkdown(text);
                            groupPreviewContent.innerHTML = `<div style="text-align: left; max-height: 200px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px;">${renderedHtml}</div>`;
                        };
                        reader.readAsText(file);
                    } else if (['txt', 'json', 'xml', 'csv'].includes(ext)) {
                        // Show text files as plain text
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            const text = e.target.result;
                            groupPreviewContent.innerHTML = `<pre style="text-align: left; max-height: 200px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 11px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
                        };
                        reader.readAsText(file);
                    } else {
                        groupPreviewContent.innerHTML = `
                            <div style="text-align: center;">
                                <div style="font-size: 64px; margin-bottom: 10px;">📄</div>
                                <div style="color: #2f3a3f; font-size: 12px;">Preview not available</div>
                            </div>
                        `;
                    }
                    
                    // Auto-upload the file to the list
                    if (!groupSelectedFiles.find(f => f.name === file.name)) {
                        groupSelectedFiles.push({file: file, name: file.name, selected: false});

                        updateGroupFilesList();
                        showMessage('File added to list', 'success');
                    }
                    
                    // Clear the input
                    if (groupFileInput) groupFileInput.value = '';
                }
            });
        }
        
        // Upload button - adds file to list
        document.getElementById('group-upload-btn')?.addEventListener('click', () => {
            if (groupPreviewFile) {
                if (!groupSelectedFiles.find(f => f.name === groupPreviewFile.name)) {
                    groupSelectedFiles.push({file: groupPreviewFile, name: groupPreviewFile.name, selected: false});

                    updateGroupFilesList();
                }
                // Clear preview
                groupPreviewFile = null;
                if (groupPreviewFilename) groupPreviewFilename.textContent = '';
                if (groupPreviewContent) groupPreviewContent.innerHTML = `<div style="font-size: 48px; color: #95a5a6;">📄</div>`;
                if (groupFileInput) groupFileInput.value = '';
            }
        });
        
        // Delete selected files
        document.getElementById('group-delete-selected-btn')?.addEventListener('click', () => {
            groupSelectedFiles = groupSelectedFiles.filter(f => !f.selected);

            updateGroupFilesList();
        });
        
        function getFileIcon(filename) {
            const ext = filename.split('.').pop().toLowerCase();
            if (['jpg', 'jpeg', 'png', 'gif'].includes(ext)) return '🖼️';
            if (ext === 'pdf') return '📕';
            if (['doc', 'docx'].includes(ext)) return '📘';
            if (ext === 'txt') return '📄';
            return '📄';
        }
        
        function getFileIconLarge(filename) {
            const ext = filename.split('.').pop().toLowerCase();
            if (['jpg', 'jpeg', 'png', 'gif'].includes(ext)) return '🖼️';
            if (ext === 'pdf') return '📕';
            if (['doc', 'docx'].includes(ext)) return '📘';
            if (ext === 'txt') return '📄';
            return '📄';
        }
        
        // Toggle file selection
        ;
        
        // Preview group file
        ;
        
        // Close modal when clicking outside
        document.getElementById('group-file-preview-modal')?.addEventListener('click', function(e) {
            if (e.target === this) {
                this.style.display = 'none';
            }
        });
        
        // Close reminder file preview modal when clicking outside
        document.getElementById('reminder-file-preview-modal')?.addEventListener('click', function(e) {
            if (e.target === this) {
                this.style.display = 'none';
            }
        });
        
        // Remove file from dropdown
        ;
        
        // Delete selected files from Files panel
        ;
        
        // Setup reminder file management (separate from groups)
        // reminderSelectedFiles is now declared at top level (line 673)


        const reminderFileInput = document.getElementById('reminder-file-input');
        const reminderFilesList = document.getElementById('reminder-files-list');
        const reminderFilesIcon = document.getElementById('reminder-files-icon');
        const reminderFileSection = document.getElementById('reminder-file-section');
        const reminderPreviewContent = document.getElementById('reminder-preview-content');
        const reminderPreviewFilename = document.getElementById('reminder-preview-filename');
        
        // Initialize updateReminderFilesList function

        window.updateReminderFilesList = updateReminderFilesList;
        
        // Initialize files list display
        updateReminderFilesList();
        
        // Show file section when clicking "add file+" button
        document.getElementById('reminder-show-file-btn')?.addEventListener('click', () => {
            const isCurrentlyHidden = reminderFileSection.style.display === 'none' || !reminderFileSection.style.display;
            if (isCurrentlyHidden) {
                reminderFileSection.style.display = 'block';
                document.getElementById('reminder-preview-panel').style.display = 'block';
                document.getElementById('reminder-files-panel').style.display = 'flex';
            } else {
                reminderFileSection.style.display = 'none';
            }
        });
        
        // Show only Files panel when clicking paperclip
        reminderFilesIcon?.addEventListener('click', () => {
            const filesPanel = document.getElementById('reminder-files-panel');
            document.getElementById('reminder-preview-panel').style.display = 'none';
            filesPanel.style.display = 'flex';
            reminderFileSection.style.display = 'block';
            updateReminderFilesList();
        });
        
        // Handle file selection - show preview and auto-upload
        if (reminderFileInput) {
            reminderFileInput.addEventListener('change', (e) => {
                const file = e.target.files[0];
                if (file) {
                    reminderPreviewFile = file;
                    const currentPreviewToken = ++reminderPreviewToken;
                    reminderPreviewFilename.textContent = file.name;
                    
                    const ext = file.name.split('.').pop().toLowerCase();
                    if (['jpg', 'jpeg', 'png', 'gif'].includes(ext)) {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            if (currentPreviewToken !== reminderPreviewToken) return;
                            reminderPreviewContent.innerHTML = `<img src="${e.target.result}" style="max-width: 100%; max-height: 200px; border-radius: 4px; object-fit: contain;">`;
                        };
                        reader.readAsDataURL(file);
                    } else if (ext === 'pdf') {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            if (currentPreviewToken !== reminderPreviewToken) return;
                            reminderPreviewContent.innerHTML = `<iframe src="${e.target.result}" title="PDF preview" style="width:100%; height:200px; border:none; border-radius:4px; background:#fff;"></iframe>`;
                        };
                        reader.readAsDataURL(file);
                    } else if (ext === 'md') {
                        // Render markdown files
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            if (currentPreviewToken !== reminderPreviewToken) return;
                            const text = e.target.result;
                            const renderedHtml = renderMarkdown(text);
                            reminderPreviewContent.innerHTML = `<div style="text-align: left; max-height: 200px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px;">${renderedHtml}</div>`;
                        };
                        reader.readAsText(file);
                    } else if (['txt', 'json', 'xml', 'csv'].includes(ext)) {
                        // Show text files as plain text
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            if (currentPreviewToken !== reminderPreviewToken) return;
                            const text = e.target.result;
                            reminderPreviewContent.innerHTML = `<pre style="text-align: left; max-height: 200px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 11px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
                        };
                        reader.readAsText(file);
                    } else {
                        reminderPreviewContent.innerHTML = `
                            <div style="text-align: center;">
                                <div style="font-size: 64px; margin-bottom: 10px;">📄</div>
                                <div style="color: #2f3a3f; font-size: 12px;">Preview not available</div>
                            </div>
                        `;
                    }
                    
                    // Auto-upload the file to the list
                    if (!reminderSelectedFiles.find(f => f.name === file.name)) {
                        reminderSelectedFiles.push({file: file, name: file.name, selected: false});
                        updateReminderFilesList();
                        showMessage('File added to list', 'success');
                    }

                    // Clear preview after adding to list
                    reminderPreviewFile = null;
                    reminderPreviewToken = 0;
                    reminderPreviewFilename.textContent = '';
                    reminderPreviewContent.innerHTML = '<div style="font-size: 48px; color: #95a5a6; margin-bottom: 10px;">📄</div>';
                    
                    // Clear the input
                    reminderFileInput.value = '';
                }
            });
        }
        
        // Upload button - adds file to list
        document.getElementById('reminder-upload-btn')?.addEventListener('click', () => {
            if (reminderPreviewFile) {
                if (!reminderSelectedFiles.find(f => f.name === reminderPreviewFile.name)) {
                    reminderSelectedFiles.push({file: reminderPreviewFile, name: reminderPreviewFile.name, selected: false});
                    updateReminderFilesList();
                }
                reminderPreviewFile = null;
                reminderPreviewToken = 0;
                reminderFileInput.value = '';
                reminderPreviewFilename.textContent = '';
                reminderPreviewContent.innerHTML = '<div style="font-size: 48px; color: #95a5a6; margin-bottom: 10px;">📄</div>';
                showMessage('File added to list', 'success');
            }
        });
        
        // Toggle file selection
        ;
        
        // Delete selected files
        ;
        
        // Load initial data with reset=true to ensure fresh load
        console.log('[INIT] Loading initial data...');
        const results = await Promise.allSettled([
            loadFiles(true),
            loadPrompts(true),
            Promise.resolve()
        ]);
        
        // Check for failures
        const failures = results.filter(r => r.status === 'rejected');
        if (failures.length > 0) {
            console.error('[INIT] Some data failed to load:', failures);
            showMessage('Warning: Some data failed to load. Check console.', 'error');
        } else {
            console.log('[INIT] Initial data loaded successfully');
        }
        
        setupInfiniteScroll();
        console.log('[INIT] Infinite scroll setup complete');

        // Auto-preview a file if ?preview=<id> is in the URL (e.g. navigated from the glasses app)
        try {
            const _previewId = new URLSearchParams(window.location.search).get('preview');
            if (_previewId) {
                // Switch to Files tab first
                document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
                document.querySelectorAll('.tab-content').forEach(t => t.classList.remove('active'));
                const filesTab = document.querySelector('[data-tab="files"]');
                const filesContent = document.getElementById('files');
                if (filesTab) filesTab.classList.add('active');
                if (filesContent) filesContent.classList.add('active');
                // Find file in loaded state or fall back to fetching metadata
                const _previewName = new URLSearchParams(window.location.search).get('name');
                const _matchedFile = state.files.data.find(f => (f.id || f.Id) === _previewId);
                const _fname = (_matchedFile && (_matchedFile.fileName || _matchedFile.FileName)) || _previewName || 'Document';
                setTimeout(() => previewFile(_previewId, _fname), 300);
            }
        } catch (e) {
            console.warn('[INIT] Auto-preview failed:', e);
        }

        console.log('[INIT] State:', JSON.stringify({
            filesCount: state.files.data.length,
            promptsCount: state.prompts.data.length,
            remindersCount: state.reminders.data.length,
            groupsCount: state.groups.data.length
        }));

        // Draft restoration disabled since auto-save is disabled
        // Conversations are persisted server-side, not in browser storage
        // if (AUTO_SAVE_ENABLED) restoreDraftConversation();

        // Conversations are already persisted server-side via ConversationService
        // No need to auto-save to ManagedPrompts on unload/hidden

        // --- Clear All History button (header) ---
        const btnDownloadAbbreviationsHeader = document.getElementById('btn-download-abbreviations-header');
        if (btnDownloadAbbreviationsHeader) {
            btnDownloadAbbreviationsHeader.addEventListener('click', () => {
                const now = Date.now();
                const downloadUrl = `${API_BASE}/abbreviations/download?_=${now}`;
                window.location.href = downloadUrl;
            });
        }


    } catch (error) {
        console.error('[INIT] Error during initialization:', error);
        showMessage('Error loading initial data: ' + error.message, 'error');
    }
}

// Wait for DOM to be ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
} else {
    init();
}

window.addEventListener('beforeunload', () => {
    stopHealthPolling();
    stopResourcePolling();
    stopGroqLivePolling();
    stopAdminSessionKeepAlive();
});
