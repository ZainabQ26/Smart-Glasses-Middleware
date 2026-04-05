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
const ADMIN_KEEPALIVE_INTERVAL_MS = 60 * 1000;
const MAX_UPLOAD_STATUS_TRANSIENTS_BEFORE_DEFER = 5;
const MIN_UPLOAD_STATUS_DEFER_RETRY_MS = 60000;
const MAX_UPLOAD_STATUS_DEFER_RETRY_MS = 300000;
// After this many deferred retries the upload is presumed permanently stuck; the user is told to re-upload.
const MAX_POLL_DEFERRED_BEFORE_ABANDON = 8;
const ADMIN_WAKE_RETRY_MS = 15000;
let adminKeepAliveTimer = null;
let adminSessionKeepAliveTimer = null;
let lastAdminWakeAttempt = 0;

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

async function fetchPreventSleepState() {
    const response = await fetch(`${API_BASE}/settings/prevent-sleep`, {
        method: 'GET',
        cache: 'no-store'
    });

    if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
    }

    return await response.json();
}

async function fetchGroqCommandTranscriptionState() {
    const response = await fetch(`${API_BASE}/settings/groq-command-transcription`, {
        method: 'GET',
        cache: 'no-store'
    });

    if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
    }

    return await response.json();
}

async function sendAdminKeepAlivePing() {
    try {
        const state = await fetchPreventSleepState();
        if (!state?.enabled) {
            const wasEnabled = !!document.getElementById('keepawake-toggle')?.checked;
            applyKeepAwakeToggle(false, false, false);

            if (wasEnabled && state?.autoDisabledAt2AmEt && typeof showMessage === 'function') {
                showMessage('Sleep prevention was turned off at 2:00 AM Eastern.', 'info');
            }
            return;
        }

        await fetch('/health', { method: 'GET', cache: 'no-store' });
    } catch (_) {
        // Keepalive is best-effort; wait for next interval.
    }
}

function stopAdminKeepAlive() {
    if (adminKeepAliveTimer) {
        clearInterval(adminKeepAliveTimer);
        adminKeepAliveTimer = null;
    }
}

function startAdminKeepAlive() {
    if (adminKeepAliveTimer) {
        return;
    }

    sendAdminKeepAlivePing();
    adminKeepAliveTimer = setInterval(sendAdminKeepAlivePing, ADMIN_KEEPALIVE_INTERVAL_MS);
}

function applyKeepAwakeToggle(enabled, showToast = false, persist = false) {
    const normalized = !!enabled;

    const toggle = document.getElementById('keepawake-toggle');
    const label = document.getElementById('keepawake-label');
    if (toggle) toggle.checked = normalized;
    if (label) label.textContent = normalized ? 'Prevent sleep: On' : 'Prevent sleep';

    if (normalized) {
        startAdminKeepAlive();
    } else {
        stopAdminKeepAlive();
    }

    if (showToast && typeof showMessage === 'function') {
        showMessage(
            normalized
                ? 'Sleep prevention enabled. This admin tab will ping the server every minute.'
                : 'Sleep prevention disabled.',
            'info'
        );
    }

    if (persist) {
        fetch(`${API_BASE}/settings/prevent-sleep`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ enabled: normalized })
        })
            .then(async (res) => {
                if (!res.ok) {
                    throw new Error(`HTTP ${res.status}`);
                }

                const payload = await res.json().catch(() => null);
                const serverEnabled = payload?.enabled;
                if (typeof serverEnabled === 'boolean' && serverEnabled !== normalized) {
                    applyKeepAwakeToggle(serverEnabled, false, false);
                }
            })
            .catch((err) => {
                console.warn('[KEEPAWAKE] Failed to persist server setting:', err);
                if (typeof showMessage === 'function') {
                    showMessage('Failed to update prevent-sleep setting on server.', 'error');
                }
            });
    }
}

function applyGroqToggle(enabled, showToast = false, persist = false) {
    const normalized = !!enabled;

    const toggle = document.getElementById('groq-toggle');
    const label = document.getElementById('groq-label');
    if (toggle) toggle.checked = normalized;
    if (label) label.textContent = normalized ? 'Groq command transcription: On' : 'Groq command transcription';

    if (showToast && typeof showMessage === 'function') {
        showMessage(
            normalized
                ? 'Groq Whisper Turbo enabled for final command transcription.'
                : 'Groq Whisper Turbo disabled. Local Whisper command transcription will be used.',
            'info'
        );
    }

    if (persist) {
        fetch(`${API_BASE}/settings/groq-command-transcription`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ enabled: normalized })
        })
            .then(async (res) => {
                if (!res.ok) {
                    throw new Error(`HTTP ${res.status}`);
                }

                const payload = await res.json().catch(() => null);
                const serverEnabled = payload?.enabled;
                if (typeof serverEnabled === 'boolean' && serverEnabled !== normalized) {
                    applyGroqToggle(serverEnabled, false, false);
                }
            })
            .catch((err) => {
                console.warn('[GROQ] Failed to persist server setting:', err);
                if (typeof showMessage === 'function') {
                    showMessage('Failed to update Groq transcription setting on server.', 'error');
                }
            });
    }
}

async function initializeKeepAwakeToggle() {
    const toggle = document.getElementById('keepawake-toggle');
    if (!toggle) {
        return;
    }

    try {
        const state = await fetchPreventSleepState();
        applyKeepAwakeToggle(!!state?.enabled, false, false);
        if (state?.autoDisabledAt2AmEt && typeof showMessage === 'function') {
            showMessage('Sleep prevention defaults to off at 2:00 AM Eastern.', 'info');
        }
    } catch (err) {
        console.warn('[KEEPAWAKE] Failed to fetch initial server setting:', err);
        applyKeepAwakeToggle(false, false, false);
    }

    toggle.addEventListener('change', () => {
        applyKeepAwakeToggle(toggle.checked, true, true);
    });
}

async function initializeGroqToggle() {
    const toggle = document.getElementById('groq-toggle');
    if (!toggle) {
        return;
    }

    try {
        const state = await fetchGroqCommandTranscriptionState();
        applyGroqToggle(!!state?.enabled, false, false);
    } catch (err) {
        console.warn('[GROQ] Failed to fetch initial server setting:', err);
        applyGroqToggle(true, false, false);
    }

    toggle.addEventListener('change', () => {
        applyGroqToggle(toggle.checked, true, true);
    });
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

function createHubReconnectDelaysMs() {
    return HUB_RECONNECT_DELAYS_MS.map(ms => ms + Math.floor(Math.random() * 2000));
}

function getHubReconnectDelayMs(retryAttempt) {
    const maxIdx = HUB_RECONNECT_DELAYS_MS.length - 1;
    const delayIdx = Math.min(Math.max(0, Number(retryAttempt) || 0), maxIdx);
    const baseDelayMs = HUB_RECONNECT_DELAYS_MS[delayIdx] || 10000;
    const jitterMs = Math.floor(Math.random() * Math.max(1000, Math.round(baseDelayMs * 0.2)));
    const outageCooldownMs = getServiceOutageCooldownMs();
    return Math.max(baseDelayMs + jitterMs, outageCooldownMs);
}

function clearConversationHubReconnectTimer() {
    if (conversationHubReconnectTimer) {
        clearTimeout(conversationHubReconnectTimer);
        conversationHubReconnectTimer = null;
    }
}

function scheduleConversationHubReconnect(reason = '') {
    if (conversationHubReconnectTimer) {
        return;
    }

    const maxIdx = HUB_RECONNECT_DELAYS_MS.length - 1;
    const delayMs = getHubReconnectDelayMs(conversationHubReconnectAttempt);
    conversationHubReconnectAttempt = Math.min(conversationHubReconnectAttempt + 1, maxIdx);

    console.warn(`[SignalR] Scheduling ConversationHub reconnect in ${delayMs}ms${reason ? ` (${reason})` : ''}`);

    conversationHubReconnectTimer = setTimeout(() => {
        conversationHubReconnectTimer = null;
        initConversationHub().catch((err) => {
            console.warn('[SignalR] Conversation hub reconnect attempt failed:', err);
            scheduleConversationHubReconnect('retry failed');
        });
    }, delayMs);
}

function clearResourceHubReconnectTimer() {
    if (resourceHubReconnectTimer) {
        clearTimeout(resourceHubReconnectTimer);
        resourceHubReconnectTimer = null;
    }
}

function scheduleResourceHubReconnect(reason = '') {
    if (resourceHubReconnectTimer) {
        return;
    }

    const maxIdx = HUB_RECONNECT_DELAYS_MS.length - 1;
    const delayMs = getHubReconnectDelayMs(resourceHubReconnectAttempt);
    resourceHubReconnectAttempt = Math.min(resourceHubReconnectAttempt + 1, maxIdx);

    console.warn(`[SignalR] Scheduling ResourceHub reconnect in ${delayMs}ms${reason ? ` (${reason})` : ''}`);

    resourceHubReconnectTimer = setTimeout(() => {
        resourceHubReconnectTimer = null;
        initResourceHub().catch((err) => {
            console.warn('[SignalR] Resource hub reconnect attempt failed:', err);
            scheduleResourceHubReconnect('retry failed');
        });
    }, delayMs);
}

async function initConversationHub() {
    if (!window.signalR) {
        console.warn('[SignalR] signalR client not loaded');
        scheduleConversationHubReconnect('signalR client not loaded');
        return;
    }

    const outageCooldownMs = getServiceOutageCooldownMs();
    if (outageCooldownMs > 0) {
        scheduleConversationHubReconnect('service unavailable cooldown');
        return;
    }

    const hubState = signalR.HubConnectionState;
    if (conversationHubConnection && hubState && (
        conversationHubConnection.state === hubState.Connected
        || conversationHubConnection.state === hubState.Connecting
        || conversationHubConnection.state === hubState.Reconnecting
    )) {
        return;
    }

    if (conversationHubConnectInProgress) {
        return;
    }

    conversationHubConnectInProgress = true;

    try {
        if (!conversationHubConnection) {
            conversationHubConnection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/conversations')
                .withAutomaticReconnect(createHubReconnectDelaysMs())
                .build();

            conversationHubConnection.on('conversationUpdated', async (payload) => {
                const convoId = payload?.conversationId || payload?.ConversationId;
                if (convoId && activeConversationId && String(convoId) === String(activeConversationId)) {
                    await loadConversationIntoView(activeConversationId);
                }
            });

            // Track reconnect events for the conversation hub and update banner state
            conversationHubConnection.onreconnecting(() => setConversationHubState(false));
            conversationHubConnection.onreconnected(() => {
                setConversationHubState(true);
                clearServiceOutage('conversation hub reconnected');
                conversationHubReconnectAttempt = 0;
                clearConversationHubReconnectTimer();
            });
            conversationHubConnection.onclose((err) => {
                setConversationHubState(false);
                if (isServiceUnavailableError(err)) {
                    noteServiceOutage('conversation hub closed');
                }
                scheduleConversationHubReconnect('connection closed');
            });
        }

        await conversationHubConnection.start();
        setConversationHubState(true);
        clearServiceOutage('conversation hub connected');
        conversationHubReconnectAttempt = 0;
        clearConversationHubReconnectTimer();
        console.log('[SignalR] Conversation hub connected');

        if (activeConversationId) {
            await joinConversationHubGroup(activeConversationId);
        }
    } catch (err) {
        console.warn('[SignalR] Failed to connect conversation hub:', err);
        if (isServiceUnavailableError(err)) {
            noteServiceOutage('conversation hub start failed');
        }
        setConversationHubState(false);
        scheduleConversationHubReconnect('start failed');
    } finally {
        conversationHubConnectInProgress = false;
    }
}

function updateSignalRBanner() {
    const warning = document.getElementById('signalr-warning');
    if (!warning) return;
    try {
        // Show banner if either hub is disconnected
        if (resourceHubConnected && conversationHubConnected) {
            warning.style.setProperty('display', 'none', 'important');
            warning.style.setProperty('visibility', 'hidden', 'important');
            warning.style.setProperty('opacity', '0', 'important');
        } else {
            warning.textContent = 'Reconnecting to server...';
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
        console.warn('[SignalR] Unable to toggle warning bar', e);
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

function clearTaskHubReconnectTimer() {
    if (taskHubReconnectTimer) {
        clearTimeout(taskHubReconnectTimer);
        taskHubReconnectTimer = null;
    }
}

function scheduleTaskHubReconnect(reason = '') {
    if (taskHubReconnectTimer) {
        return;
    }

    const maxIdx = HUB_RECONNECT_DELAYS_MS.length - 1;
    const delayMs = getHubReconnectDelayMs(taskHubReconnectAttempt);
    taskHubReconnectAttempt = Math.min(taskHubReconnectAttempt + 1, maxIdx);

    console.warn(`[SignalR] Scheduling TaskHub reconnect in ${delayMs}ms${reason ? ` (${reason})` : ''}`);

    taskHubReconnectTimer = setTimeout(() => {
        taskHubReconnectTimer = null;
        initTaskHub().catch((err) => {
            console.warn('[SignalR] Task hub reconnect attempt failed:', err);
            scheduleTaskHubReconnect('retry failed');
        });
    }, delayMs);
}

// Backwards-compatible wrapper used by tests and any callers that expect the old API
function setSignalRIndicatorState(connected) {
    // Backwards-compatible API used by tests and external callers.
    // Toggle the banner immediately for deterministic behavior and update internal flags.
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
            warning.textContent = 'Reconnecting to server...';
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
        console.warn('[SignalR] Unable to toggle warning bar (compat wrapper)', e);
    }
}

async function initResourceHub() {
    if (!window.signalR) {
        console.warn('[SignalR] signalR client not loaded');
        scheduleResourceHubReconnect('signalR client not loaded');
        return;
    }

    const outageCooldownMs = getServiceOutageCooldownMs();
    if (outageCooldownMs > 0) {
        scheduleResourceHubReconnect('service unavailable cooldown');
        return;
    }

    const hubState = signalR.HubConnectionState;
    if (resourceHubConnection && hubState && (
        resourceHubConnection.state === hubState.Connected
        || resourceHubConnection.state === hubState.Connecting
        || resourceHubConnection.state === hubState.Reconnecting
    )) {
        return;
    }

    if (resourceHubConnectInProgress) {
        return;
    }

    resourceHubConnectInProgress = true;

    try {
        if (!resourceHubConnection) {
            resourceHubConnection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/resources')
                .withAutomaticReconnect(createHubReconnectDelaysMs())
                .build();

            resourceHubConnection.on('filesChanged', async () => {
                apiCache.invalidate('files');
                apiCache.invalidate('batch');
                await loadFiles(true);
            });

            resourceHubConnection.on('groupsChanged', async () => {
                apiCache.invalidate('groups');
                apiCache.invalidate('batch');
                await Promise.resolve();
                state.allGroups = [];
            });

            resourceHubConnection.on('conversationsChanged', async () => {
                apiCache.invalidate('prompts');
                apiCache.invalidate('batch');
                await loadPrompts(true);
            });

            resourceHubConnection.onreconnecting(() => setResourceHubState(false));
            resourceHubConnection.onreconnected(() => {
                setResourceHubState(true);
                clearServiceOutage('resource hub reconnected');
                resourceHubReconnectAttempt = 0;
                clearResourceHubReconnectTimer();
            });
            resourceHubConnection.onclose((err) => {
                setResourceHubState(false);
                if (isServiceUnavailableError(err)) {
                    noteServiceOutage('resource hub closed');
                }
                scheduleResourceHubReconnect('connection closed');
            });
        }

        await resourceHubConnection.start();
        setResourceHubState(true);
        clearServiceOutage('resource hub connected');
        resourceHubReconnectAttempt = 0;
        clearResourceHubReconnectTimer();
        console.log('[SignalR] Resource hub connected');
    } catch (err) {
        console.warn('[SignalR] Failed to connect resource hub:', err);
        if (isServiceUnavailableError(err)) {
            noteServiceOutage('resource hub start failed');
        }
        setResourceHubState(false);
        scheduleResourceHubReconnect('start failed');
    } finally {
        resourceHubConnectInProgress = false;
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

async function initTaskHub() {
    if (!window.signalR) {
        console.warn('[SignalR] signalR client not loaded');
        scheduleTaskHubReconnect('signalR client not loaded');
        return;
    }

    const outageCooldownMs = getServiceOutageCooldownMs();
    if (outageCooldownMs > 0) {
        scheduleTaskHubReconnect('service unavailable cooldown');
        return;
    }

    const hubState = signalR.HubConnectionState;
    if (taskHubConnection && hubState && (
        taskHubConnection.state === hubState.Connected
        || taskHubConnection.state === hubState.Connecting
        || taskHubConnection.state === hubState.Reconnecting
    )) {
        return;
    }

    if (taskHubConnectInProgress) {
        return;
    }

    taskHubConnectInProgress = true;

    try {
        if (!taskHubConnection) {
            taskHubConnection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/tasks')
                .withAutomaticReconnect(createHubReconnectDelaysMs())
                .build();

            // Handle request status updates
            taskHubConnection.on('RequestUpdate', (request) => {
                console.log('[TaskHub] Request update:', request);
                const requestId = String(getProp(request, 'requestId') || '').trim();
                if (requestId) {
                    activeRequests.set(requestId, request);
                }
                updateProgressDisplay(request);
                applyTaskHubUploadStatus(request);
            });

            // Handle detailed activity updates
            taskHubConnection.on('ActivityUpdate', (activity) => {
                console.log('[TaskHub] Activity update:', activity);
                const requestId = String(getProp(activity, 'requestId') || '').trim();
                if (!requestId) {
                    return;
                }
                if (!requestActivities.has(requestId)) {
                    requestActivities.set(requestId, []);
                }
                requestActivities.get(requestId).push({
                    timestamp: getProp(activity, 'timestamp') || new Date().toISOString(),
                    activity: getProp(activity, 'activity'),
                    progress: getProp(activity, 'progress')
                });
                updateActivityDisplay(requestId);
                applyTaskHubUploadActivity(activity);
            });

            taskHubConnection.onreconnecting((err) => {
                console.warn('[SignalR] Task hub reconnecting:', err);
            });
            taskHubConnection.onreconnected(() => {
                taskHubReconnectAttempt = 0;
                clearTaskHubReconnectTimer();
                clearServiceOutage('task hub reconnected');
                console.log('[SignalR] Task hub reconnected');
            });
            taskHubConnection.onclose((err) => {
                console.warn('[SignalR] Task hub closed:', err);
                if (isServiceUnavailableError(err)) {
                    noteServiceOutage('task hub closed');
                }
                scheduleTaskHubReconnect('connection closed');
            });
        }

        await taskHubConnection.start();
        taskHubReconnectAttempt = 0;
        clearTaskHubReconnectTimer();
        clearServiceOutage('task hub connected');
        console.log('[SignalR] Task hub connected');
    } catch (err) {
        console.warn('[SignalR] Failed to connect task hub:', err);
        if (isServiceUnavailableError(err)) {
            noteServiceOutage('task hub start failed');
        }
        scheduleTaskHubReconnect('start failed');
    } finally {
        taskHubConnectInProgress = false;
    }
}

function updateProgressDisplay(request) {
    const requestId = String(getProp(request, 'requestId') || '').trim();
    if (!requestId) return;

    const progress = Number(getProp(request, 'progress') || 0);
    const details = getProp(request, 'details') || getProp(request, 'status') || '';

    // Find any active progress bars and update them
    const progressBars = document.querySelectorAll('[data-request-id="' + requestId + '"]');
    progressBars.forEach(bar => {
        if (bar.classList.contains('progress-bar')) {
            bar.style.width = progress + '%';
            bar.setAttribute('aria-valuenow', progress);
        }
        if (bar.classList.contains('progress-text')) {
            bar.textContent = details;
        }
        if (bar.classList.contains('progress-percentage')) {
            bar.textContent = progress + '%';
        }
    });
}

function updateActivityDisplay(requestId) {
    const activityList = document.getElementById('activity-history-' + requestId);
    if (!activityList) return;

    const activities = requestActivities.get(requestId) || [];
    activityList.innerHTML = activities.slice(-5).reverse().map(act => `
        <div class="activity-item" style="padding: 4px 0; font-size: 12px; color: #666;">
            <span style="font-weight: 500;">${act.activity}</span>
            ${act.progress ? `<span style="float: right;">${act.progress}%</span>` : ''}
        </div>
    `).join('');
}

async function joinConversationHubGroup(conversationId) {
    if (!conversationHubConnection || !conversationId) return;
    try {
        await conversationHubConnection.invoke('JoinConversation', String(conversationId));
    } catch (err) {
        console.warn('[SignalR] Failed to join conversation group:', err);
    }
}

// Tab switching with forced refresh
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
        tab.classList.add('active');
        document.getElementById(tab.dataset.tab).classList.add('active');
        
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
    loading.innerHTML = '<p style="text-align: center; padding: 20px; color: #2f3a3f;">Loading...</p>';
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
                    list.innerHTML = '<div class="empty-state"><p>Server is temporarily unavailable. File list will refresh automatically.</p></div>';
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
                list.innerHTML = '<div class="empty-state"><p>Server is busy. File list will refresh automatically.</p></div>';
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
            list.innerHTML = '<div class="empty-state"><p>No uploads yet</p></div>';
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
    const label = (rawLabel && rawLabel !== 'Untitled') ? rawLabel : (fileName !== 'unknown' ? fileName : 'Untitled');
    const fileSize = getProp(file, 'fileSize') || 0;
    const uploadedAt = getProp(file, 'uploadedAt');
    const groups = getProp(file, 'groups') || [];
    
    div.innerHTML = `
        <div class="card-header">
            <div class="card-title">${escapeHtml(label)}</div>
        </div>
        <div class="card-meta">
            File: ${escapeHtml(fileName)} | 
            Size: ${formatFileSize(fileSize)} | 
            Uploaded: ${uploadedAt ? new Date(uploadedAt).toLocaleString() : 'Unknown'}
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
                responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">No recent files found. Upload one first, then try again.</div>';
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
    
    previewContent.innerHTML = '<p style="color: #2f3a3f;">Loading preview...</p>';
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
        previewContent.innerHTML = '<p style="color: #e74c3c;">No preview available for this file type. Use download.</p>';
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
        filesList.innerHTML = '<div style="color: #2f3a3f; text-align: center; padding: 20px; font-size: 14px;">No uploads to show</div>';
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

document.getElementById('group-form').addEventListener('submit', async (e) => {
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
    modalContent.innerHTML = '<p style="color: #95a5a6; text-align: center; padding: 20px;">Loading preview...</p>';
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
        initializeKeepAwakeToggle();
        initializeGroqToggle();
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
        document.getElementById('close-preview').addEventListener('click', () => {
            document.getElementById('file-preview').style.display = 'none';
        });
        document.getElementById('download-file').addEventListener('click', () => {
            if (currentPreviewFile.id) {
                const downloadUrl = `${API_BASE}/files/${currentPreviewFile.id}/download`;
                window.location.href = downloadUrl;
            } else {
                showMessage('No file selected for download', 'error');
            }
        });

        // Stagger hub startup to avoid concurrent negotiate spikes during cold starts.
        initConversationHub();
        setTimeout(() => initResourceHub(), 750);
        setTimeout(() => initTaskHub(), 1500);
        
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
        
        // Reset attach-file section to closed state on every page load
        if (promptFileSection) {
            promptFileSection.style.setProperty('display', 'none', 'important');
            promptFileSection.style.setProperty('visibility', 'hidden', 'important');
        }
        const _initAttachBtn = document.getElementById('prompt-show-file-btn');
        if (_initAttachBtn) {
            _initAttachBtn.textContent = 'Attach file';
            // Remove inline onclick to prevent double-toggle (onclick + addEventListener both firing)
            _initAttachBtn.removeAttribute('onclick');
        }
        // Same fix for the paperclip icon
        if (promptFilesIcon) promptFilesIcon.removeAttribute('onclick');

        updatePromptFilesList();
        
        // Show file section when clicking "add file+" button
        document.getElementById('prompt-show-file-btn')?.addEventListener('click', () => {
            if (promptUploadInFlight) {
                showMessage('Please wait for the current upload to finish.', 'info');
                return;
            }
            if (ADMIN_DEBUG) console.log('prompt-show-file-btn clicked');
            // Prefer a global shim if present (ensures reliability across browsers/load timing)
            if (typeof window.togglePromptFileSection === 'function') {
                if (ADMIN_DEBUG) console.log('calling window.togglePromptFileSection()');
                try {
                    const before = window.getComputedStyle(promptFileSection);
                    const wasHidden = before
                        ? (before.display === 'none' || before.visibility === 'hidden' || before.opacity === '0')
                        : (promptFileSection.style.display === 'none' || !promptFileSection.style.display);

                    window.togglePromptFileSection();

                    const after = window.getComputedStyle(promptFileSection);
                    const isHiddenAfter = after
                        ? (after.display === 'none' || after.visibility === 'hidden' || after.opacity === '0')
                        : (promptFileSection.style.display === 'none' || !promptFileSection.style.display);

                    if (wasHidden && !isHiddenAfter) {
                        const _showBtn = document.getElementById('prompt-show-file-btn');
                        if (_showBtn) _showBtn.textContent = '✕ Close';
                        return;
                    }

                    if (!wasHidden && isHiddenAfter) {
                        const _closeBtn = document.getElementById('prompt-show-file-btn');
                        if (_closeBtn) _closeBtn.textContent = 'Attach file';
                        return;
                    }
                } catch (e) { /* fallback below */ }
            }
            // Fallback: toggle using computed style for robustness
            const computed = window.getComputedStyle(promptFileSection);
            const isCurrentlyHidden = computed
                ? (computed.display === 'none' || computed.visibility === 'hidden' || computed.opacity === '0')
                : (promptFileSection.style.display === 'none' || !promptFileSection.style.display);
            
            if (isCurrentlyHidden) {
                promptFileSection.style.setProperty('display', 'block', 'important');
                promptFileSection.style.setProperty('visibility', 'visible', 'important');
                promptFileSection.style.setProperty('opacity', '1', 'important');
                const _showBtn = document.getElementById('prompt-show-file-btn');
                if (_showBtn) _showBtn.textContent = '✕ Close';
                const previewPanel = document.getElementById('prompt-preview-panel');
                const filesPanel = document.getElementById('prompt-files-panel');
                if (previewPanel) previewPanel.style.setProperty('display', 'block', 'important');
                if (filesPanel) filesPanel.style.setProperty('display', 'flex', 'important');
                if (ADMIN_DEBUG) console.log('promptFileSection toggled -> shown (computed):', window.getComputedStyle(promptFileSection).display, 'visibility:', window.getComputedStyle(promptFileSection).visibility);
                try { if (promptFileSection && typeof promptFileSection._markShown === 'function') promptFileSection._markShown(); } catch(e){}
                // Ensure the actual file input is visible even if tab-content remains hidden due to timing
                try {
                    const inputEl = document.getElementById('prompt-file-input');
                    if (inputEl) {
                        const tabContent = inputEl.closest('.tab-content');
                        if (tabContent && window.getComputedStyle(tabContent).display === 'none') {
                            if (!inputEl.__originalParent) {
                                inputEl.__originalParent = inputEl.parentNode;
                                inputEl.__originalNext = inputEl.nextSibling;
                            }
                            document.body.appendChild(inputEl);
                            inputEl.style.setProperty('position', 'absolute', 'important');
                            inputEl.style.setProperty('left', '10px', 'important');
                            inputEl.style.setProperty('top', '80px', 'important');
                            inputEl.style.setProperty('z-index', '10000', 'important');
                        }
                    }
                } catch(e) { if (ADMIN_DEBUG) console.warn('ensureFileInputVisible failed', e); }
            } else {
                promptFileSection.style.setProperty('display', 'none', 'important');
                promptFileSection.style.setProperty('visibility', 'hidden', 'important');
                promptFileSection.style.setProperty('opacity', '0', 'important');
                const _closeBtn = document.getElementById('prompt-show-file-btn');
                if (_closeBtn) _closeBtn.textContent = 'Attach file';
                if (ADMIN_DEBUG) console.log('promptFileSection toggled -> hidden (computed):', window.getComputedStyle(promptFileSection).display, 'visibility:', window.getComputedStyle(promptFileSection).visibility);
                // Try to restore file input to its original parent when hiding
                try {
                    const inputEl = document.getElementById('prompt-file-input');
                    if (inputEl && inputEl.__originalParent) {
                        inputEl.__originalParent.insertBefore(inputEl, inputEl.__originalNext || null);
                        inputEl.style.removeProperty('position');
                        inputEl.style.removeProperty('left');
                        inputEl.style.removeProperty('top');
                        inputEl.style.removeProperty('z-index');
                        delete inputEl.__originalParent;
                        delete inputEl.__originalNext;
                    }
                } catch(e) { /* noop */ }
            }
        });
        
        // Show only Files panel when clicking paperclip in Prompts
        promptFilesIcon?.addEventListener('click', () => {
            if (promptUploadInFlight) {
                showMessage('Please wait for the current upload to finish.', 'info');
                return;
            }
            if (ADMIN_DEBUG) console.log('promptFilesIcon clicked (paperclip)');
            // Prefer global shim when available
            if (typeof window.showPromptFilesPanel === 'function') {
                if (ADMIN_DEBUG) console.log('calling window.showPromptFilesPanel()');
                try {
                    window.showPromptFilesPanel();
                    const filesPanelEl = document.getElementById('prompt-files-panel');
                    const sectionState = window.getComputedStyle(promptFileSection);
                    const filesState = filesPanelEl ? window.getComputedStyle(filesPanelEl) : null;
                    const shimSucceeded = sectionState.display !== 'none'
                        && sectionState.visibility !== 'hidden'
                        && sectionState.opacity !== '0'
                        && (!filesState || filesState.display !== 'none');

                    if (shimSucceeded) {
                        updatePromptFilesList();
                        const _paperclipShowBtn = document.getElementById('prompt-show-file-btn');
                        if (_paperclipShowBtn) _paperclipShowBtn.textContent = '✕ Close';
                        return;
                    }
                } catch (e) { /* fallback below */ }
            }
            const filesPanel = document.getElementById('prompt-files-panel');
            // Hide preview panel, show only files panel
            document.getElementById('prompt-preview-panel').style.setProperty('display', 'none', 'important');
            filesPanel.style.setProperty('display', 'flex', 'important');
            filesPanel.setAttribute('data-context', 'prompts');

            promptFileSection.style.setProperty('display', 'block', 'important');
            promptFileSection.style.setProperty('visibility', 'visible', 'important');
            promptFileSection.style.setProperty('opacity', '1', 'important');
            if (ADMIN_DEBUG) console.log('promptFilesIcon flow set promptFileSection display:block (computed):', window.getComputedStyle(promptFileSection).display, 'visibility:', window.getComputedStyle(promptFileSection).visibility);
            // Ensure file input is visible even if parent tab not active yet
            try {
                const inputEl = document.getElementById('prompt-file-input');
                if (inputEl) {
                    const tabContent = inputEl.closest('.tab-content');
                    if (tabContent && window.getComputedStyle(tabContent).display === 'none') {
                        if (!inputEl.__originalParent) {
                            inputEl.__originalParent = inputEl.parentNode;
                            inputEl.__originalNext = inputEl.nextSibling;
                        }
                        document.body.appendChild(inputEl);
                        inputEl.style.setProperty('position', 'absolute', 'important');
                        inputEl.style.setProperty('left', '10px', 'important');
                        inputEl.style.setProperty('top', '80px', 'important');
                        inputEl.style.setProperty('z-index', '10000', 'important');
                    }
                }
            } catch(e) { if (ADMIN_DEBUG) console.warn('ensureFileInputVisible(paperclip) failed', e); }
            
            // Update files list after showing the panel
            updatePromptFilesList();
            const _paperclipShowBtn = document.getElementById('prompt-show-file-btn');
            if (_paperclipShowBtn) _paperclipShowBtn.textContent = '✕ Close';
        });
        
        // Handle file selection - show preview and auto-upload
        if (promptFileInput) {
            promptFileInput.addEventListener('change', async (e) => {
                const file = e.target.files[0];
                if (file) {
                    if (promptUploadInFlight) {
                        showMessage('A file upload is already in progress. Please wait.', 'info');
                        promptFileInput.value = '';
                        return;
                    }

                    promptPreviewFile = file;
                    if (promptPreviewFilename) {
                        promptPreviewFilename.textContent = file.name;
                    }
                    
                    const ext = file.name.split('.').pop().toLowerCase();
                    // Keep upload independent from preview rendering.
                    if (promptPreviewContent) {
                        promptPreviewContent.innerHTML = `
                            <div style="text-align: center;">
                                <div style="font-size: 48px; margin-bottom: 10px;">⬆</div>
                                <div style="color: #2f3a3f; font-size: 12px;">Uploading first. Preview will be available from the file list after upload.</div>
                            </div>
                        `;
                    }
                    
                    // Auto-upload the file
                    // Check if file already exists in list
                    if (promptSelectedFiles.find(f => f.name === file.name)) {
                        showMessage('File already uploaded', 'error');
                        promptFileInput.value = '';
                        return;
                    }

                    const pendingFile = {
                        file,
                        name: file.name,
                        selected: false,
                        pending: true,
                        statusText: 'Uploading to server...',
                        uploadProgress: 0,
                        isManagedFile: false,
                        managedFileId: null,
                        documentId: null,
                        _availabilityMonitorStarted: false
                    };
                    promptSelectedFiles.push(pendingFile);
                    syncPromptSelectedFilesState();

                    promptUploadInFlight = true;
                    setPromptUploadLoading(true);
                    
                    try {
                        const progressBar = document.getElementById('prompt-upload-progress-bar');
                        if (progressBar) {
                            progressBar.style.width = '0%';
                        }
                        
                        // Audio files are not supported
                        if (['wav', 'mp3', 'm4a', 'ogg', 'webm'].includes(ext)) {
                            showMessage('File type not supported', 'error');
                            promptFileInput.value = '';
                            return;
                        }

                        // ── Presigned S3 upload (bypasses CloudFront 60s timeout) ──
                        // Step 1: ask the API for a presigned PUT URL
                        let presignedUrlData = null;
                        try {
                            const urlResp = await fetch(`${API_BASE}/files/upload-url?filename=${encodeURIComponent(file.name)}&contentType=${encodeURIComponent(file.type || 'application/octet-stream')}`);
                            if (urlResp.ok) {
                                presignedUrlData = await urlResp.json();
                            }
                            // 501 = not available in this env → fall through to legacy flow
                        } catch (_) { /* network error → fall through */ }

                        let fileData;
                        const usedPresignedUpload = !!(presignedUrlData && presignedUrlData.presignedUrl);

                        if (usedPresignedUpload) {
                            // Step 2: PUT directly to S3 (no CloudFront, no ECS)
                            await new Promise((resolve, reject) => {
                                const s3xhr = new XMLHttpRequest();
                                s3xhr.timeout = 600000; // 10 min for large files
                                s3xhr.upload.addEventListener('progress', (e) => {
                                    if (e.lengthComputable && progressBar) {
                                        const pct = (e.loaded / e.total) * 100;
                                        progressBar.style.width = pct + '%';
                                        pendingFile.uploadProgress = pct;
                                        pendingFile.statusText = 'Uploading to storage...';
                                        syncPromptSelectedFilesState();
                                    }
                                });
                                s3xhr.addEventListener('load', () => {
                                    if (s3xhr.status >= 200 && s3xhr.status < 300) resolve();
                                    else reject(new Error(`S3 upload failed (${s3xhr.status})`));
                                });
                                s3xhr.addEventListener('error', () => reject(new Error('Network error during S3 upload')));
                                s3xhr.addEventListener('abort', () => reject(new Error('Upload cancelled')));
                                s3xhr.addEventListener('timeout', () => reject(new Error('S3 upload timed out')));
                                s3xhr.open('PUT', presignedUrlData.presignedUrl);
                                s3xhr.setRequestHeader('Content-Type', file.type || 'application/octet-stream');
                                s3xhr.send(file);
                            });

                            if (progressBar) progressBar.style.width = '100%';

                            // Step 3: tell the API the upload is done
                            const clientFileId = createClientGuid();
                            pendingFile.registrationFileId = clientFileId;
                            pendingFile.expectedSize = file.size;
                            pendingFile.uploadProgress = 100;
                            pendingFile.statusText = 'Finalizing upload...';
                            syncPromptSelectedFilesState();

                            fileData = await registerPresignedUpload({
                                fileId: clientFileId,
                                key: presignedUrlData.key,
                                fileName: file.name,
                                contentType: file.type || 'application/octet-stream',
                                label: file.name,
                                fileSize: file.size
                            });

                        } else {
                            // ── Legacy proxied upload (fallback for LocalStack / MinIO) ──
                            const formData = new FormData();
                            formData.append('file', file);
                            formData.append('label', file.name || 'Attachment');
                            
                            const xhr = new XMLHttpRequest();
                            xhr.timeout = 240000;
                            
                            xhr.upload.addEventListener('progress', (e) => {
                                if (e.lengthComputable && progressBar) {
                                    const percentComplete = (e.loaded / e.total) * 100;
                                    progressBar.style.width = percentComplete + '%';
                                    pendingFile.uploadProgress = percentComplete;
                                    pendingFile.statusText = 'Uploading to server...';
                                    syncPromptSelectedFilesState();
                                }
                            });
                            
                            fileData = await new Promise((resolve, reject) => {
                                xhr.addEventListener('load', () => {
                                    if (progressBar) progressBar.style.width = '100%';
                                    if (xhr.status === 504) {
                                        reject(new Error('Server timed out while processing this file. Please retry in a moment.'));
                                        return;
                                    }
                                    if (xhr.status >= 200 && xhr.status < 300) {
                                        try {
                                            resolve(JSON.parse(xhr.responseText));
                                        } catch (e) {
                                            reject(new Error('Invalid JSON response'));
                                        }
                                    } else {
                                        const responsePreview = (xhr.responseText || '').slice(0, 240);
                                        reject(new Error(`Upload failed (${xhr.status}): ${responsePreview}`));
                                    }
                                });
                                xhr.addEventListener('error', () => reject(new Error('Network error')));
                                xhr.addEventListener('abort', () => reject(new Error('Upload cancelled')));
                                xhr.addEventListener('timeout', () => reject(new Error('Upload timed out while waiting for the server')));
                                xhr.open('POST', '/api/admin/files');
                                xhr.send(formData);
                            });
                        } // end legacy upload branch

                        const managedFileId = fileData.id || fileData.Id;
                        const uploadRequestId = fileData.requestId || fileData.RequestId || null;
                        const uploadStatus = (fileData.status || fileData.Status || '').toLowerCase();
                        const registrationState = (fileData.registration || fileData.Registration || '').toLowerCase();
                        const registrationDeferred = registrationState === 'deferred'
                            || registrationState === 'queued'
                            || registrationState === 'in-progress'
                            || registrationState === 'processing';
                        const isQueued = uploadStatus === 'processing';

                        pendingFile.id = managedFileId;
                        pendingFile.managedFileId = managedFileId;
                        pendingFile.requestId = uploadRequestId;
                        pendingFile.isManagedFile = true;
                        pendingFile.uploadProgress = 100;

                        if (usedPresignedUpload && managedFileId) {
                            // Keep the file pending until the backend confirms it is ready.
                            pendingFile.pending = true;
                            pendingFile.statusText = registrationDeferred
                                ? 'Upload accepted. Finalizing server registration...'
                                : 'Upload completed. Confirming server readiness...';
                            pendingFile._availabilityMonitorStarted = false;
                            syncPromptSelectedFilesState();
                            showMessage(`File "${file.name}" uploaded. Waiting for server confirmation...`, 'info');

                            monitorPromptFileAvailability(pendingFile).catch(error => {
                                console.warn('Prompt file availability monitor failed', error);
                            });
                        } else {
                            pendingFile.statusText = usedPresignedUpload
                                ? 'Upload completed. Confirming server registration...'
                                : (isQueued ? 'Finishing upload...' : 'Verifying file access...');
                            syncPromptSelectedFilesState();
                            showMessage(`File "${file.name}" is uploading. It will move to recent files once ready.`, 'info');

                            monitorPromptFileAvailability(pendingFile).catch(error => {
                                console.warn('Prompt file availability monitor failed', error);
                            });
                        }
                        
                        // Clear preview
                        promptPreviewFile = null;
                        if (promptPreviewFilename) promptPreviewFilename.textContent = '';
                        if (promptPreviewContent) promptPreviewContent.innerHTML = `<div style="font-size: 48px; color: #95a5a6;">📄</div>`;
                        if (promptFileInput) promptFileInput.value = '';
                    } catch (error) {
                        console.error('Error uploading file:', error);
                        const uploadErrorText = String(error?.message || '').toLowerCase();
                        const isRecoverableRegistrationError = uploadErrorText.includes('timed out')
                            || uploadErrorText.includes('timeout')
                            || uploadErrorText.includes('network')
                            || uploadErrorText.includes('502')
                            || uploadErrorText.includes('503')
                            || uploadErrorText.includes('504')
                            || uploadErrorText.includes('bad gateway')
                            || uploadErrorText.includes('service unavailable')
                            || uploadErrorText.includes('gateway timeout')
                            || uploadErrorText.includes('aborterror')
                            || uploadErrorText.includes('aborted');

                        if (isRecoverableRegistrationError) {
                            pendingFile.id = null;
                            pendingFile.managedFileId = null;
                            pendingFile.isManagedFile = false;
                            pendingFile.expectedSize = file.size;
                            pendingFile.statusText = 'Upload completed. Confirming server registration...';
                            pendingFile.uploadProgress = 100;
                            syncPromptSelectedFilesState();
                            showMessage(`Upload confirmation was interrupted for "${file.name}". Checking server state automatically...`, 'info');

                            monitorPromptFileAvailability(pendingFile, 300000, 15000).catch(monitorError => {
                                console.warn('Prompt upload timeout recovery failed', monitorError);
                            });

                            promptPreviewFile = null;
                            if (promptPreviewFilename) promptPreviewFilename.textContent = '';
                            if (promptPreviewContent) promptPreviewContent.innerHTML = `<div style="font-size: 48px; color: #95a5a6;">📄</div>`;
                            if (promptFileInput) promptFileInput.value = '';
                            return;
                        }

                        promptSelectedFiles = promptSelectedFiles.filter(f => f !== pendingFile);
                        syncPromptSelectedFilesState();
                        showMessage(`Failed to upload file: ${error.message}`, 'error');
                        if (promptFileInput) promptFileInput.value = '';
                    } finally {
                        promptUploadInFlight = false;
                        setPromptUploadLoading(false);
                    }
                }
            });
        }
        
        // Global abort controller for cancelling requests
        let currentRequestController = null;
        let currentRequestAbortReason = null;
        let currentRequestTimeoutMs = null;
        
        // Send button function - sends prompt to API
        const buildSourcesHtml = (sources) => {
            if (!sources || sources.length === 0) return '';
            return `
                <div style="margin-top: 15px; padding: 10px; background: white; border: 1px solid #dee2e6; border-radius: 4px;">
                    <div style="font-weight: 600; color: #2c3e50; margin-bottom: 8px; font-size: 0.9rem;">
                        Sources Used (${sources.length}):
                    </div>
                    <div style="display: flex; flex-direction: column; gap: 6px;">
                        ${sources.map((source, index) => {
                            const documentId = source.documentId || source.DocumentId;
                            const fileName = source.fileName || source.FileName || 'Document';
                            const documentType = source.documentType || source.DocumentType || 'unknown';
                            const page = source.pageNumber || source.PageNumber;
                            const chunkText = source.chunkText || source.ChunkText || '';
                            const label = page ? `${fileName} (p.${page})` : fileName;
                            const href = documentId ? `/api/documents/${documentId}/download` : '#';
                            const linkStyle = documentId ? 'color: #0b63a6; text-decoration: underline;' : 'color: #95a5a6; text-decoration: none;';
                            const sourceId = 'source-' + Date.now() + '-' + index;

                            // Determine file type icon
                            const typeIcon = documentType === 'pdf' ? '📄'
                                : documentType === 'markdown' ? '📝'
                                    : documentType === 'image' ? '🖼️'
                                        : documentType === 'docx' ? '📘'
                                            : documentType === 'xml' ? '🔖' : '📋';

                            return `
                                <div style="border: 1px solid #e0e0e0; border-radius: 4px; padding: 8px; background: #fafafa;">
                                    <div style="display: flex; align-items: center; justify-content: space-between;">
                                        <div style="display: flex; align-items: center; gap: 8px; flex: 1;">
                                            <span style="font-size: 18px;">${typeIcon}</span>
                                            <div style="display: flex; flex-direction: column; gap: 2px;">
                                                <a href="${href}" target="_blank" style="${linkStyle} font-weight: 500;" onclick="${documentId ? '' : 'return false;'}">${escapeHtml(label)}</a>
                                                <span style="font-size: 11px; color: #999;">Type: ${escapeHtml(documentType)}</span>
                                            </div>
                                        </div>
                                        <div style="display: flex; gap: 4px;">
                                            ${documentId ? `<button type="button" onclick="previewDocumentFile('${documentId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}'); return false;" style="padding: 4px 10px; background: #0b63a6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;">Preview</button>` : ''}
                                            ${chunkText ? `<button type="button" onclick="toggleSourceExpand('${sourceId}'); return false;" style="padding: 4px 10px; background: #6c757d; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;" data-testid="expand-source-${index}">Expand</button>` : ''}
                                        </div>
                                    </div>
                                    ${chunkText ? `<div id="${sourceId}" style="display: none; margin-top: 8px; padding: 8px; background: white; border-left: 3px solid #0b63a6; border-radius: 2px; font-size: 12px; color: #666; line-height: 1.4;">${escapeHtml(chunkText)}</div>` : ''}
                                </div>
                            `;
                        }).join('')}
                    </div>
                </div>
            `;
        };
        
        // Toggle source expansion
        window.toggleSourceExpand = function(sourceId) {
            const element = document.getElementById(sourceId);
            if (element) {
                const isHidden = element.style.display === 'none';
                element.style.display = isHidden ? 'block' : 'none';
                
                // Update button text
                const button = document.querySelector(`[onclick*="${sourceId}"]`);
                if (button) {
                    button.textContent = isHidden ? 'Collapse' : 'Expand';
                }
            }
        };

        // Helper to normalize the command text (strips wake-word if accidentally provided)
        const normalizeCommandText = (value) => {
            const raw = (value || '').trim();
            if (!raw) return '';
            // Since admin chat doesn't enforce a keyword, if the user explicitly types it,
            // we should try to strip it if we know the configured keyword.
            // We strip common patterns (e.g. "iris", "hi iris") so it behaves identically
            // to the homepage when users habitually type the keyword.
            const wakeWords = ["hi iris", "iris", "hey iris"];
            let cleaned = raw;
            for (const word of wakeWords) {
                const wordEsc = word.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
                const rx = new RegExp(`^${wordEsc}[\\s,:-]*`, "i");
                if (rx.test(cleaned)) {
                    cleaned = cleaned.replace(rx, "").trim();
                    break;
                }
            }
            return cleaned || raw;
        };

        const sendPrompt = async () => {
            const rawPromptText = document.getElementById('prompt-name')?.value;
            const promptText = normalizeCommandText(rawPromptText);

            const responseContent = document.getElementById('response-content');
            const sendBtn = document.getElementById('prompt-send-btn');
            const cancelBtn = document.getElementById('prompt-cancel-btn');
            let lastQueryResult = null;
            
            if (!promptText || !promptText.trim()) {
                showMessage('Please enter a prompt text', 'error');
                return;
            }

            const pendingRecentChoice = resolvePendingRecentFileChoice(promptText);
            if (pendingRecentChoice.state === 'out-of-range') {
                responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap; color:#e67e22;">I only have ${pendingRecentChoice.max} recent files in the current list. Choose a number from 1 to ${pendingRecentChoice.max}.</div>`;
                showMessage(`Choose a number between 1 and ${pendingRecentChoice.max}`, 'info');
                return;
            }

            if (pendingRecentChoice.state === 'selected' && pendingRecentChoice.file) {
                const selected = pendingRecentChoice.file;
                const opened = await openFileFromAction(selected.id, selected.fileName, responseContent);
                if (opened) {
                    clearPendingRecentFileChoices();
                }

                document.getElementById('prompt-name').value = '';
                return;
            }

            if (isDirectRecentDocumentPreviewCommand(promptText)) {
                await openMostRecentDocumentPreview(responseContent);
                document.getElementById('prompt-name').value = '';
                return;
            }

            // Local PDF navigation commands matching App.jsx
            const lowered = promptText.toLowerCase().trim();
            if (/\b(?:next page|go to next page|page down)\b/.test(lowered)) {
                responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">Next page</div>';
                if (window.pdfViewer && window.pdfViewer.pdfDocument) {
                    const currentPage = window.pdfViewer.currentPageNumber || 1;
                    const totalPages = window.pdfViewer.pagesCount || 1;
                    if (currentPage < totalPages) {
                        window.pdfViewer.currentPageNumber = currentPage + 1;
                    }
                }
                document.getElementById('prompt-name').value = '';
                return;
            }

            if (/\b(?:previous page|prev page|go to previous page|back page)\b/.test(lowered)) {
                responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">Previous page</div>';
                if (window.pdfViewer && window.pdfViewer.pdfDocument) {
                    const currentPage = window.pdfViewer.currentPageNumber || 1;
                    if (currentPage > 1) {
                        window.pdfViewer.currentPageNumber = currentPage - 1;
                    }
                }
                document.getElementById('prompt-name').value = '';
                return;
            }

            const goToPageMatch = lowered.match(/\b(?:go to|goto|open)?\s*page\s+(\d{1,4})\b/);
            if (goToPageMatch) {
                const pageNumber = parseInt(goToPageMatch[1], 10);
                if (!Number.isNaN(pageNumber)) {
                    responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;">Go to page ${pageNumber}</div>`;
                    if (window.pdfViewer && window.pdfViewer.pdfDocument) {
                        window.pdfViewer.currentPageNumber = pageNumber;
                    }
                    document.getElementById('prompt-name').value = '';
                    return;
                }
            }

            if (/\bclose\s+(?:the\s+)?(?:document|pdf)\b/.test(lowered)) {
                if (document.getElementById('file-preview')) document.getElementById('file-preview').style.display = 'none';
                if (document.getElementById('reminder-file-preview-modal')) document.getElementById('reminder-file-preview-modal').style.display = 'none';
                responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">Closed document preview</div>';
                document.getElementById('prompt-name').value = '';
                return;
            }

            const pendingPromptFiles = promptSelectedFiles.filter(f => f.pending);
            if (pendingPromptFiles.length > 0) {
                const pendingLabel = pendingPromptFiles.length === 1
                    ? `${pendingPromptFiles[0].name || pendingPromptFiles[0].file?.name || 'The file'} is still uploading.`
                    : `${pendingPromptFiles.length} files are still uploading.`;
                responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap; color:#e67e22;">${escapeHtml(pendingLabel)} Wait until they finish and become ready before sending the prompt.</div>`;
                showMessage('Wait for attached files to finish uploading', 'info');
                return;
            }
            
            try {
                // Generate a request ID for tracking
                const requestId = 'prompt-' + Date.now() + '-' + Math.random().toString(36).substr(2, 9);
                
                // Show progress bar instead of hourglass
                responseContent.innerHTML = `
                    <div style="padding: 15px;">
                        <div class="progress-text" data-request-id="${requestId}" data-testid="progress-status">Initializing...</div>
                        <div class="progress-bar-container">
                            <div class="progress-bar" data-request-id="${requestId}" role="progressbar" aria-valuenow="0" aria-valuemin="0" aria-valuemax="100" style="width: 0%">
                                <span class="progress-percentage" data-request-id="${requestId}">0%</span>
                            </div>
                        </div>
                        <div class="activity-history" id="activity-history-${requestId}" style="display: none;">
                            <!-- Activity items will be inserted here -->
                        </div>
                    </div>
                `;
                
                // Show activity history after a short delay
                setTimeout(() => {
                    const activityHistory = document.getElementById('activity-history-' + requestId);
                    if (activityHistory) activityHistory.style.display = 'block';
                }, 500);
                
                // Show cancel button, hide send button
                if (sendBtn) sendBtn.style.display = 'none';
                if (cancelBtn) cancelBtn.style.display = 'block';
                
                // Send query to documents endpoint. Use server-expected property names (Query/TopK)
                const documentsUrl = '/api/documents/query';
                const selectedFiles = promptSelectedFiles.some(f => f.selected)
                    ? promptSelectedFiles.filter(f => f.selected)
                    : promptSelectedFiles;
                const documentIds = [];
                for (const f of selectedFiles) {
                    if (f.isManagedFile) {
                        continue;
                    }
                    let id = f.documentId || f.id;
                    if (!id && f.name) {
                        id = await resolveDocumentIdByName(f.name);
                        if (id) f.documentId = id;
                    }
                    if (id) documentIds.push(id);
                }
                const includeAllGroups = document.getElementById('prompt-include-all')?.checked;
                const payload = { Query: promptText, TopK: 5, RequestId: requestId };
                if (documentIds.length > 0) {
                    payload.DocumentIds = documentIds;
                }

                // Include prior conversation turns so the AI can maintain context.
                // Keep in sync with MaxHistoryTurns on the backend (DocumentsController.cs).
                const MAX_HISTORY_TURNS = 10;
                if (currentConversationHistory.length > 0) {
                    payload.ConversationHistory = currentConversationHistory.slice(-MAX_HISTORY_TURNS).map(msg => ({
                        Role: msg.role === 'user' ? 'user' : 'assistant',
                        Content: msg.content
                    }));
                }


                // Never upload files during Send. Attachments must already be registered by the
                // auto-upload flow so sending a prompt cannot trigger a second heavy upload request.
                if (promptSelectedFiles && promptSelectedFiles.length > 0) {
                    const unresolvedFiles = promptSelectedFiles.filter(f => {
                        const hasManagedRegistration = !!(f.isManagedFile && (f.managedFileId || f.id));
                        const hasDocumentRegistration = !!(!f.isManagedFile && (f.documentId || f.id));
                        return !hasManagedRegistration && !hasDocumentRegistration;
                    });

                    if (unresolvedFiles.length > 0) {
                        const unresolvedPlural = unresolvedFiles.length === 1 ? 'is' : 'are';
                        const unresolvedLabel = unresolvedFiles.length === 1
                            ? (unresolvedFiles[0].name || unresolvedFiles[0].file?.name || 'Attached file')
                            : `${unresolvedFiles.length} attached files`;
                        responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap; color:#e67e22;">${escapeHtml(unresolvedLabel)} ${unresolvedPlural} still being registered. Wait a moment, then send again.</div>`;
                        showMessage('Waiting for file registration before sending prompt', 'info');
                        return;
                    }
                }

                // Recompute selected files and documentIds after any uploads
                const selectedFilesAfterUpload = promptSelectedFiles.some(f => f.selected)
                    ? promptSelectedFiles.filter(f => f.selected)
                    : promptSelectedFiles;
                const documentIdsAfterUpload = selectedFilesAfterUpload
                    .map(f => f.isManagedFile ? null : (f.documentId || f.id))
                    .filter(Boolean);
                if (documentIdsAfterUpload.length > 0) {
                    payload.DocumentIds = documentIdsAfterUpload;
                }

                console.log('[SEND PROMPT] Sending documents query to', documentsUrl, 'payload:', payload);

                currentRequestController = new AbortController();
                currentRequestAbortReason = null;
                currentRequestTimeoutMs = 300000; // 300 seconds
                const timeoutId = setTimeout(() => {
                    currentRequestAbortReason = 'timeout';
                    try { currentRequestController.abort(); } catch (e) { /* noop */ }
                }, currentRequestTimeoutMs);

                const queryResponse = await fetch(documentsUrl, {
                    method: 'POST',
                    headers: {'Content-Type': 'application/json', 'Accept': 'application/json'},
                    body: JSON.stringify(payload),
                    signal: currentRequestController.signal
                });
                clearTimeout(timeoutId);
                currentRequestController = null;

                console.log('[SEND PROMPT] documents query status:', queryResponse.status, queryResponse.statusText);
                const rawResponseText = await queryResponse.text().catch(() => '');
                let result = null;
                if (rawResponseText) {
                    try {
                        result = JSON.parse(rawResponseText);
                        lastQueryResult = result;
                        console.log('[SEND PROMPT] documents query response JSON:', result);
                    } catch (parseErr) {
                        console.error('[SEND PROMPT] Failed to parse JSON response. Raw response:', rawResponseText);
                        if (!queryResponse.ok) {
                            const normalizedBody = rawResponseText.replace(/\s+/g, ' ').trim();
                            const timeoutLikeResponse = queryResponse.status === 504 || /gateway timeout/i.test(normalizedBody);
                            if (timeoutLikeResponse) {
                                throw new Error('The request timed out before the server responded. The selected document may still be processing.');
                            }

                            throw new Error(`Server error: ${queryResponse.status} ${queryResponse.statusText}`);
                        }

                        throw new Error('Server returned a non-JSON response');
                    }
                }

                if (!queryResponse.ok) {
                    const errorMsg = result?.error || result?.details || result?.message || `Server error: ${queryResponse.status}`;
                    throw new Error(errorMsg);
                }

                if (!result) {
                    throw new Error('Empty response from server');
                }


                const answer = result.answer || result.Answer || result.AnswerText || result?.AnswerHtml || result?.answerText || 'No response received';
                const normalizedAnswer = String(answer || '').trim();

                // Intercept file picker action
                if (normalizedAnswer.includes('__ACTION:OPEN_FILE_PICKER__')) {
                    const fileInput = document.getElementById('file-input');
                    if (fileInput) fileInput.click();
                    return;
                }

                // Intercept recent files action and render a numbered list in the chat pane.
                if (normalizedAnswer.includes('__ACTION:SHOW_RECENT_FILES__')) {
                    try {
                        const recentFiles = await fetchRecentFilesForAction(5);
                        if (!recentFiles.length) {
                            clearPendingRecentFileChoices();
                            responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">No recent files found. Upload one first, then try again.</div>';
                            return;
                        }

                        setPendingRecentFileChoices(recentFiles);
                        renderRecentFilesAction(responseContent, recentFiles);
                    } catch (error) {
                        clearPendingRecentFileChoices();
                        responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap; color:#e74c3c;">Could not load recent files right now.</div>';
                    }
                    return;
                }

                // Intercept select document action — navigate to Files tab and open preview
                const selectDocumentMatch = normalizedAnswer.match(/__ACTION:SELECT_DOCUMENT:([0-9a-fA-F-]{36}):([\s\S]*)$/i);
                if (selectDocumentMatch) {
                    const fileId = selectDocumentMatch[1];
                    const fileName = selectDocumentMatch[2].trim().replace(/^['"]+|['"]+$/g, '');
                    await openFileFromAction(fileId, fileName, responseContent);
                    clearPendingRecentFileChoices();
                    return;
                }

                // Intercept PDF page navigation actions
                if (normalizedAnswer.includes('__ACTION:NEXT_PDF_PAGE__')) {
                    responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">Next page</div>';
                    // Send message to PDF viewer iframe/embed to go to next page
                    if (window.pdfViewer && window.pdfViewer.pdfDocument) {
                        const currentPage = window.pdfViewer.currentPageNumber || 1;
                        const totalPages = window.pdfViewer.pagesCount || 1;
                        if (currentPage < totalPages) {
                            window.pdfViewer.currentPageNumber = currentPage + 1;
                        }
                    }
                    return;
                }

                if (normalizedAnswer.includes('__ACTION:PREVIOUS_PDF_PAGE__')) {
                    responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">Previous page</div>';
                    // Send message to PDF viewer iframe/embed to go to previous page
                    if (window.pdfViewer && window.pdfViewer.pdfDocument) {
                        const currentPage = window.pdfViewer.currentPageNumber || 1;
                        if (currentPage > 1) {
                            window.pdfViewer.currentPageNumber = currentPage - 1;
                        }
                    }
                    return;
                }

                if (normalizedAnswer.includes('__ACTION:CLOSE_DOCUMENT__')) {
                    if (document.getElementById('file-preview')) document.getElementById('file-preview').style.display = 'none';
                    if (document.getElementById('reminder-file-preview-modal')) document.getElementById('reminder-file-preview-modal').style.display = 'none';
                    responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">Closed document</div>';
                    return;
                }

                if (normalizedAnswer.startsWith('__ACTION:GO_TO_PAGE:')) {
                    const rawPage = normalizedAnswer.replace("__ACTION:GO_TO_PAGE:", "").replace(/__$/, "").trim();
                    const pageNumber = parseInt(rawPage, 10);
                    responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;">Go to page ${pageNumber}</div>`;
                    if (window.pdfViewer && window.pdfViewer.pdfDocument && !Number.isNaN(pageNumber)) {
                        window.pdfViewer.currentPageNumber = pageNumber;
                    }
                    return;
                }

                if (normalizedAnswer.startsWith('__ACTION:SHOW_REMINDERS:')) {
                    const jsonStr = normalizedAnswer.replace("__ACTION:SHOW_REMINDERS:", "").replace(/__$/, "").trim();
                    try {
                        const reminders = JSON.parse(jsonStr);
                        const listHtml = (Array.isArray(reminders) ? reminders : [])
                            .map((r, idx) => `<li>${escapeHtml(r.text)}</li>`).join('');
                        responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;"><strong>Your Reminders:</strong><ul style="margin-left: 20px; margin-top: 10px;">${listHtml || '<li>No reminders yet.</li>'}</ul></div>`;
                    } catch (_) {
                        responseContent.innerHTML = '<div style="padding: 15px; white-space: pre-wrap;">Couldn\'t parse reminders. Please try again.</div>';
                    }
                    return;
                }

                const sources = result.referencedSources || result.ReferencedSources || result.sources || result.Sources || [];

                // Get current attached files
                const attachedFiles = promptSelectedFiles.map(f => ({
                    name: f.file?.name || f.name,
                    id: f.documentId || f.id
                }));
                
                // Add to conversation history with files and sources
                currentConversationHistory.push({role: 'user', content: promptText, files: attachedFiles});
                currentConversationHistory.push({role: 'bot', content: answer, sources});


                // Determine if backend marked this response as produced by advanced model
                const usedAdvancedGlobal = result?.usedAdvancedModel ?? result?.UsedAdvancedModel ?? result?.usedAdvanced ?? false;

                // Display the full conversation history
                const conversationHTML = currentConversationHistory.map(msg => {
                    const filesData = msg.files && msg.files.length > 0
                        ? encodeURIComponent(JSON.stringify(msg.files))
                        : '';
                    const filesTitle = msg.files && msg.files.length > 0
                        ? `${msg.files.length} file(s) attached: ${msg.files.map(f => f.name).join(', ')}`
                        : '';
                    const msgSources = Array.isArray(msg.sources) ? msg.sources : [];
                    const msgSourcesHtml = msg.role === 'bot' && msgSources.length > 0
                        ? buildSourcesHtml(msgSources)
                        : '';
                    // If this is a bot message and server indicated advanced model was used for the result,
                    // render a small badge directly underneath the message content.
                    const advancedBadgeHtml = (msg.role === 'bot' && usedAdvancedGlobal)
                        ? `<div class="advanced-badge-inline" style="margin-top:8px; display:inline-flex; align-items:center; gap:6px; font-size:12px; color:#0b63a6;
                                background: rgba(11,99,166,0.06); padding:4px 8px; border-radius:6px;">⚡ Advanced</div>`
                        : '';

                    return `
                        <div class="conversation-message" style="margin-bottom: 12px;">
                            <div class="conversation-message-role ${msg.role === 'user' ? 'conversation-message-user' : 'conversation-message-bot'}" style="font-weight: 600; margin-bottom: 4px; display: flex; align-items: center; gap: 8px;">
                                <span>${msg.role === 'user' ? 'User' : 'Bot'}:</span>
                                ${msg.files && msg.files.length > 0 ? `<button type="button" onclick="showAttachedFilesModal('${filesData}')" style="background: none; border: none; font-size: 16px; color: #0b63a6; cursor: pointer; padding: 0;" title="${escapeHtml(filesTitle)}">📎</button>` : ''}
                            </div>
                            <div class="conversation-message-content" style="padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
                            ${advancedBadgeHtml}
                            ${msgSourcesHtml}
                        </div>
                    `;
                }).join('');
                responseContent.innerHTML = `
                    <div class="conversation-history-container" style="max-height: 500px; overflow-y: auto; padding: 15px; border-radius: 8px;">
                        ${conversationHTML}
                    </div>
                `;

                // If the backend indicates the advanced model was used, show a small badge
                try {
                    const usedAdvanced = result?.usedAdvancedModel ?? result?.UsedAdvancedModel ?? result?.usedAdvanced ?? false;
                    if (usedAdvanced) {
                        const container = responseContent.querySelector('.conversation-history-container');
                        if (container) {
                            // Ensure container is positioned so the badge can be absolutely positioned inside it
                            container.style.position = 'relative';
                            const badge = document.createElement('div');
                            badge.className = 'advanced-badge';
                            badge.title = 'Generated by advanced model';
                            badge.style.position = 'absolute';
                            badge.style.right = '8px';
                            badge.style.bottom = '8px';
                            badge.style.background = 'rgba(11,99,166,0.9)';
                            badge.style.color = 'white';
                            badge.style.padding = '4px 6px';
                            badge.style.borderRadius = '6px';
                            badge.style.fontSize = '12px';
                            badge.style.display = 'flex';
                            badge.style.alignItems = 'center';
                            badge.style.gap = '6px';
                            badge.innerHTML = '<span style="font-size:12px;">⚡</span><span style="font-size:11px;">Advanced</span>';
                            container.appendChild(badge);
                        }
                    }
                } catch (e) {
                    console.warn('Failed to render advanced badge', e);
                }
                
                // Clear input for next message
                document.getElementById('prompt-name').value = '';
                
                showMessage('Response received successfully', 'success');

                apiCache.invalidate('/api/conversations');
                await loadPrompts(true);
                await Promise.resolve();


                // Conversations are already persisted server-side via ConversationService
                // No need to auto-save to ManagedPrompts (which are for reusable templates)
                // try {
                //     await autoSaveCurrentConversation('send');
                // } catch (err) {
                //     console.warn('[SEND PROMPT] Auto-save after send failed:', err);
                // }

                // Clear attached files after sending
                promptSelectedFiles = [];
                window.promptSelectedFiles = promptSelectedFiles;
                updatePromptFilesList();
                
            } catch (error) {
                console.error('Error sending prompt:', error);
                if (error.name === 'AbortError') {
                    const timedOut = currentRequestAbortReason === 'timeout';
                    responseContent.innerHTML = `
                        <div style="text-align: center; padding: 20px; color: ${timedOut ? '#e74c3c' : '#e67e22'};">
                            <div style="font-size: 1.2rem; margin-bottom: 10px;">${timedOut ? '⏱️ Timed out' : '🚫 Cancelled'}</div>
                            <div style="font-size: 0.9rem;">${timedOut ? `Request timed out after ${Math.round((currentRequestTimeoutMs || 0) / 1000)}s. Please try again or shorten the request.` : 'Request successfully cancelled.'}</div>
                        </div>
                    `;
                    showMessage(timedOut ? 'Request timed out' : 'Request successfully cancelled', timedOut ? 'error' : 'info');
                } else {
                    const fallbackSources = lastQueryResult?.referencedSources || lastQueryResult?.ReferencedSources || lastQueryResult?.sources || lastQueryResult?.Sources || [];
                    const fallbackSourcesHtml = buildSourcesHtml(fallbackSources);
                    responseContent.innerHTML = `
                        <div style="text-align: center; padding: 20px; color: #e74c3c;">
                            <div style="font-size: 1.2rem; margin-bottom: 10px;">❌ Error</div>
                            <div style="font-size: 0.9rem;">${escapeHtml(error.message)}</div>
                        </div>
                        ${fallbackSourcesHtml}
                    `;
                        showMessage(`Error: ${error.message}`, 'error');
                    }
                } finally {
                // Reset buttons regardless of success or error
                currentRequestController = null;
                currentRequestAbortReason = null;
                currentRequestTimeoutMs = null;
                if (sendBtn) sendBtn.style.display = 'block';
                if (cancelBtn) cancelBtn.style.display = 'none';
            }
        };
        
        // Send button click handler
        document.getElementById('prompt-send-btn')?.addEventListener('click', sendPrompt);
        
        // Cancel button click handler
        document.getElementById('prompt-cancel-btn')?.addEventListener('click', () => {
            if (currentRequestController) {
                currentRequestAbortReason = 'cancel';
                currentRequestController.abort();
                showMessage('Cancelling request...', 'info');
            }
        });
        
        // Enter key handler for prompt text input
        document.getElementById('prompt-name')?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                sendPrompt();
            }
        });

        // Form submit handler - prevent default and call sendPrompt
        document.getElementById('prompt-form')?.addEventListener('submit', (e) => {
            e.preventDefault();
            sendPrompt();
        });

        // Conversations are already persisted server-side
        // Auto-save to ManagedPrompts is unnecessary
        // document.getElementById('prompt-name')?.addEventListener('input', () => {
        //     autoSaveCurrentConversation('input');
        // });
        
        // New Chat button - saves current conversation to Auto Groups
        document.getElementById('new-chat-btn')?.addEventListener('click', async () => {
            if (currentConversationHistory.length === 0) {
                showMessage('No conversation to save', 'error');
                return;
            }
            
            // Get the first user message as the name
            const firstMessage = currentConversationHistory.find(m => m.role === 'user');
            if (!firstMessage) {
                showMessage('No user messages to save', 'error');
                return;
            }
            const chatName = firstMessage.content.substring(0, 50);
            
            // Get file details from uploaded files
            const fileDetails = promptSelectedFiles.map(f => ({
                name: f.name,
                id: f.documentId || f.id
            }));
            
            // Create JSON string with conversation and files
            const conversationData = JSON.stringify({
                messages: currentConversationHistory,
                files: fileDetails
            });
            
            try {
                // Conversations are already persisted server-side
                // No need to save to ManagedPrompts
                // await autoSaveCurrentConversation('new_chat');
                
                // Reset for new chat
                currentConversationHistory = [];
                promptSelectedFiles = [];
                window.promptSelectedFiles = promptSelectedFiles;
                updatePromptFilesList();

                updateActiveGroupStyles();
                
                const promptIdInput = document.getElementById('prompt-id');
                if (promptIdInput) promptIdInput.value = '';
                document.getElementById('prompt-name').value = '';
                document.getElementById('response-content').innerHTML = '<span style="color: #2f3a3f;">Enter a prompt and click Send to get a response</span>';
                
                // Reload prompts list to show conversations
                loadPrompts(true);
                
            } catch (error) {
                console.error('Error starting new conversation:', error);
                showMessage('Failed to start new conversation', 'error');
            }
        });
        
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

        // --- Clear All History button (header) ---
        const btnClearAllHeader = document.getElementById('btn-clear-all-header');
        if (btnClearAllHeader) {
            btnClearAllHeader.addEventListener('click', async () => {
                if (!confirm(
                    'This will permanently delete ALL data:\n\n' +
                    '• Request monitoring records\n' +
                    '• All uploaded files\n' +
                    '• All conversations and messages\n' +
                    '• All documents and RAG entries\n' +
                    '• AI interaction logs\n' +
                    '• All resource groups (including reminders)\n' +
                    '• All prompt templates\n\n' +
                    'Nothing will be preserved.\n\n' +
                    'This cannot be undone. Continue?'
                )) return;

                btnClearAllHeader.disabled = true;
                btnClearAllHeader.textContent = 'Clearing…';
                try {
                    const res = await fetch(`${API_BASE}/clear-all`, { method: 'POST' });
                    if (!res.ok) {
                        const err = await res.json().catch(() => ({ title: res.statusText }));
                        throw new Error(err.detail || err.title || 'Request failed');
                    }
                    showMessage('All history cleared successfully.', 'success');
                    // Reset active group/conversation state


                    currentConversationHistory = [];
                    promptSelectedFiles = [];
                    window.promptSelectedFiles = [];
                    state.allGroups = [];
                    updateActiveGroupStyles();
                    // Clear the conversation view
                    const responseContent = document.getElementById('response-content');
                    if (responseContent) {
                        responseContent.innerHTML = '<span style="color: #2f3a3f;">Enter a message and click Send to get a response</span>';
                    }
                    // Immediately wipe all tab containers and reset pagination/loading state so
                    // the load functions below are not blocked by an in-progress loading guard
                    // and the user sees empty state right away without waiting for the next fetch.
                    const emptyGroups = '<div class="empty-state"><p>No groups created yet</p></div>';
                    const emptyFiles  = '<div class="empty-state"><p>No files uploaded</p></div>';
                    const emptyPrompts = '<div class="empty-state"><p>No prompts saved</p></div>';
                    const emptyReminders = '<div class="empty-state"><p>No reminders</p></div>';
                    const groupsEl   = document.getElementById('groups-list');
                    const filesEl    = document.getElementById('files-list');
                    const promptsEl  = document.getElementById('prompts-list');
                    const remindersEl = document.getElementById('reminders-list');
                    if (groupsEl)   groupsEl.innerHTML   = emptyGroups;
                    if (filesEl)    filesEl.innerHTML    = emptyFiles;
                    if (promptsEl)  promptsEl.innerHTML  = emptyPrompts;
                    if (remindersEl) remindersEl.innerHTML = emptyReminders;
                    // Reset all pagination state and force-clear loading guards so the
                    // load functions below don't bail out due to an in-progress load.
                    for (const key of ['files', 'prompts', 'groups', 'reminders']) {
                        state[key].loading = false;
                        state[key].data    = [];
                        state[key].page    = 1;
                        state[key].hasMore = true;
                    }
                    // Re-fetch from server to confirm empty state and update pagination
                    await Promise.all([
                        loadFiles(true),
                        loadPrompts(true),
                        Promise.resolve(),

                    ]).catch(() => {});
                    updatePromptFilesList();
                } catch (err) {
                    console.error('[ClearAll] failed:', err);
                    showMessage('Failed to clear history: ' + err.message, 'error');
                } finally {
                    btnClearAllHeader.disabled = false;
                    btnClearAllHeader.textContent = '🗑 Clear All History';
                }
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
    stopAdminKeepAlive();
    stopAdminSessionKeepAlive();
});
