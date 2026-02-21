const API_BASE = '/api/admin';
const ADMIN_DEBUG = !!window.__ADMIN_DEBUG__;
const themeStorageKey = 'uiTheme';

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

async function fetchWithRetry(url, options = {}, retries = 2) {
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
        headers: {
            ...options.headers,
            'Content-Type': options.headers?.['Content-Type'] || 'application/json'
        }
    };
    
    // Create the fetch promise
    const fetchPromise = (async () => {
        for (let i = 0; i < retries; i++) {
            try {
                console.log(`[API] Request: ${method} ${url}${i > 0 ? ` (retry ${i})` : ''}`);

                const response = await fetch(bustUrl, fetchOptions);
                console.log(`[API] Response: ${response.status} ${response.statusText} for ${url}`);

                // Clone response for caching
                if (response.ok && method === 'GET') {
                    const clone = response.clone();
                    clone.json().then(data => {
                        apiCache.set(cacheKey, data);
                    }).catch(() => {
                        // Ignore cache errors
                    });
                }

                if (!response.ok && response.status >= 500 && i < retries - 1) {
                    console.warn(`[API] Server error, retrying in ${(i + 1) * 500}ms...`);
                    await new Promise(resolve => setTimeout(resolve, (i + 1) * 500));
                    continue;
                }

                return response;
            } catch (error) {
                console.error(`[API] Network error on attempt ${i + 1}:`, error);
                if (i < retries - 1) {
                    await new Promise(resolve => setTimeout(resolve, (i + 1) * 500));
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

// Track individual hub connection states; banner shows if either hub is disconnected
// Default to `true` so the banner is hidden until a hub explicitly reports disconnected.
let resourceHubConnected = true;
let conversationHubConnected = true;

async function initConversationHub() {
    if (!window.signalR) {
        console.warn('[SignalR] signalR client not loaded');
        return;
    }

    try {
        conversationHubConnection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/conversations')
            .withAutomaticReconnect()
            .build();

        conversationHubConnection.on('conversationUpdated', async (payload) => {
            const convoId = payload?.conversationId || payload?.ConversationId;
            if (convoId && activeConversationId && String(convoId) === String(activeConversationId)) {
                await loadConversationIntoView(activeConversationId);
            }
        });

        // Track reconnect events for the conversation hub and update banner state
        conversationHubConnection.onreconnecting(() => setConversationHubState(false));
        conversationHubConnection.onreconnected(() => setConversationHubState(true));
        conversationHubConnection.onclose(() => setConversationHubState(false));

        await conversationHubConnection.start();
        setConversationHubState(true);
        console.log('[SignalR] Conversation hub connected');

        if (activeConversationId) {
            await joinConversationHubGroup(activeConversationId);
        }
    } catch (err) {
        console.warn('[SignalR] Failed to connect conversation hub:', err);
        setConversationHubState(false);
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
        return;
    }

    try {
        resourceHubConnection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/resources')
            .withAutomaticReconnect()
            .build();

        resourceHubConnection.on('filesChanged', async () => {
            apiCache.invalidate('files');
            apiCache.invalidate('batch');
            await loadFiles(true);
        });

        resourceHubConnection.on('groupsChanged', async () => {
            apiCache.invalidate('groups');
            apiCache.invalidate('batch');
            await loadGroups(true);
            await loadAllGroups(true);
        });

        resourceHubConnection.on('conversationsChanged', async () => {
            apiCache.invalidate('prompts');
            apiCache.invalidate('batch');
            await loadPrompts(true);
        });

        resourceHubConnection.onreconnecting(() => setResourceHubState(false));
        resourceHubConnection.onreconnected(() => setResourceHubState(true));
        resourceHubConnection.onclose(() => setResourceHubState(false));

        await resourceHubConnection.start();
        setResourceHubState(true);
        console.log('[SignalR] Resource hub connected');
    } catch (err) {
        console.warn('[SignalR] Failed to connect resource hub:', err);
        setResourceHubState(false);
    }
}

// Track active requests for progress display
const activeRequests = new Map();
const requestActivities = new Map();

async function initTaskHub() {
    if (!window.signalR) {
        console.warn('[SignalR] signalR client not loaded');
        return;
    }

    try {
        taskHubConnection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/tasks')
            .withAutomaticReconnect()
            .build();

        // Handle request status updates
        taskHubConnection.on('RequestUpdate', (request) => {
            console.log('[TaskHub] Request update:', request);
            activeRequests.set(request.requestId, request);
            updateProgressDisplay(request);
        });

        // Handle detailed activity updates
        taskHubConnection.on('ActivityUpdate', (activity) => {
            console.log('[TaskHub] Activity update:', activity);
            if (!requestActivities.has(activity.requestId)) {
                requestActivities.set(activity.requestId, []);
            }
            requestActivities.get(activity.requestId).push({
                timestamp: activity.timestamp || new Date().toISOString(),
                activity: activity.activity,
                progress: activity.progress
            });
            updateActivityDisplay(activity.requestId);
        });

        await taskHubConnection.start();
        console.log('[SignalR] Task hub connected');
    } catch (err) {
        console.warn('[SignalR] Failed to connect task hub:', err);
    }
}

function updateProgressDisplay(request) {
    // Find any active progress bars and update them
    const progressBars = document.querySelectorAll('[data-request-id="' + request.requestId + '"]');
    progressBars.forEach(bar => {
        if (bar.classList.contains('progress-bar')) {
            bar.style.width = (request.progress || 0) + '%';
            bar.setAttribute('aria-valuenow', request.progress || 0);
        }
        if (bar.classList.contains('progress-text')) {
            bar.textContent = request.details || request.status || '';
        }
        if (bar.classList.contains('progress-percentage')) {
            bar.textContent = (request.progress || 0) + '%';
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
            loadGroups(true);
            loadAllGroups();
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
        const response = await fetchWithRetry(`${API_BASE}/files?page=${state.files.page}&pageSize=${state.files.pageSize}`);
        
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        
        const result = await response.json();
        console.log('Files API response:', result);
        // Handle both lowercase and uppercase from API
        const filesData = result.data || result.Data || [];
        const pagination = result.pagination || result.Pagination || {};
        console.log('Files data length:', filesData.length);
        console.log('Files pagination:', pagination);
        
        const list = document.getElementById('files-list');
        
        hideLoading('files-list');
        
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
async function loadPrompts(reset = false) {
    if (state.prompts.loading) return;
    if (!reset && !state.prompts.hasMore) return;

    if (reset) {
        state.prompts.page = 1;
        state.prompts.hasMore = true;
        state.prompts.data = [];
        const listEl = document.getElementById('prompts-list');
        if (listEl) listEl.innerHTML = '';
    }

    state.prompts.loading = true;
    showLoading('prompts-list');

    try {
        // Always load conversations (not groups)
        const endpoint = `/api/conversations?page=${state.prompts.page}&pageSize=${state.prompts.pageSize}`;

        const response = await fetchWithRetry(endpoint);
        if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);

        const result = await response.json();
        const items = Array.isArray(result) ? result : (result.data || result.Data || []);
        const pagination = Array.isArray(result) ? {} : (result.pagination || result.Pagination || {});

        hideLoading('prompts-list');

        const list = document.getElementById('prompts-list');
        if (items && items.length > 0) {
            state.prompts.data.push(...items);
            const fragment = document.createDocumentFragment();
            items.forEach(item => {
                try {
                    const div = document.createElement('div');
                    div.className = 'sidebar-prompt-item';

                    // These are always conversations (not groups)
                    const isGroup = false;

                    const id = getProp(item, 'id') || getProp(item, 'Id');
                    const title = getProp(item, 'title') || getProp(item, 'Title') || getProp(item, 'name') || getProp(item, 'Name') || id;
                    const updated = getProp(item, 'updatedAt') || getProp(item, 'UpdatedAt') || getProp(item, 'lastUpdated') || '';
                    // derive messages info when present
                    const messagesArr = getProp(item, 'messages') || getProp(item, 'Messages') || getProp(item, 'MessagesList') || [];
                    const messageCount = Array.isArray(messagesArr) ? messagesArr.length : (getProp(item, 'messageCount') || getProp(item, 'MessageCount') || 0);
                    const attachmentCountRaw = getProp(item, 'attachmentCount') || getProp(item, 'AttachmentCount');
                    const attachmentCount = attachmentCountRaw !== undefined && attachmentCountRaw !== null
                        ? Number(attachmentCountRaw)
                        : null;
                    let fileCount = Number.isFinite(attachmentCount) ? attachmentCount : 0;

                    if (!Number.isFinite(attachmentCount) && Array.isArray(messagesArr)) {
                        fileCount = messagesArr.reduce((sum, msg) => {
                            const rawFiles = Array.isArray(msg.files) ? msg.files
                                : (Array.isArray(msg.Files) ? msg.Files
                                    : (Array.isArray(msg.Attachments)
                                        ? msg.Attachments
                                        : (Array.isArray(msg.attachments) ? msg.attachments : [])));
                            return sum + normalizeFileList(rawFiles).length;
                        }, 0);
                    }
                    const lastMsg = (Array.isArray(messagesArr) && messagesArr.length > 0)
                        ? (messagesArr[messagesArr.length - 1].content || messagesArr[messagesArr.length - 1].text || messagesArr[messagesArr.length - 1].message || '')
                        : (getProp(item, 'lastMessage') || getProp(item, 'snippet') || getProp(item, 'description') || '');

                    div.dataset.conversationId = id;
                    div.dataset.groupId = getProp(item, 'resourceGroupId') || getProp(item, 'ResourceGroupId') || '';

                    // Don't show group badges in conversation list anymore
                    
                    const displayName = escapeHtml(title || 'Untitled');
                    const timestamp = updated ? new Date(updated).toLocaleString(undefined, {
                        month: 'short',
                        day: 'numeric',
                        hour: '2-digit',
                        minute: '2-digit'
                    }) : '';
                    
                    div.innerHTML = `
                        <div style="display:block;">
                            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:4px;gap:6px;">
                                <input class="prompt-name-input" value="${displayName}" data-conversation-id="${id}" readonly
                                    style="font-weight:600;color:#2c3e50;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:65%;border:1px solid transparent;background:transparent;padding:0;margin:0;outline:none;" />
                                <div style="display:flex;gap:6px;align-items:center;">
                                    <button type="button" class="conversation-edit-btn" title="Rename conversation" aria-label="Rename conversation" style="border:none;background:#e0e0e0;color:#2c3e50;padding:2px 6px;border-radius:10px;cursor:pointer;font-size:11px;">Edit</button>
                                    <span class="badge" data-conversation-files="${id}" title="${fileCount} file(s) attached" style="background:#0b63a6;color:white;padding:2px 6px;border-radius:10px;font-size:11px;min-width:20px;text-align:center;">${fileCount}</span>
                                    <button type="button" class="conversation-delete-btn" title="Delete conversation" aria-label="Delete conversation" style="border:none;background:#e74c3c;color:white;width:20px;height:20px;border-radius:50%;cursor:pointer;font-size:14px;line-height:20px;padding:0;">×</button>
                                </div>
                            </div>
                            <div style="font-size:11px;color:#2f3a3f;margin-bottom:4px;">${timestamp}</div>
                            <div style="color:#444;font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${escapeHtml(String(lastMsg || 'No messages yet'))}</div>
                        </div>
                    `;

                    div.addEventListener('click', async () => {
                        document.querySelectorAll('.sidebar-prompt-item').forEach(i => i.classList.remove('active'));
                        div.classList.add('active');
                        activeConversationId = id;
                        await syncActiveGroupFromConversation(id);
                        await loadConversationIntoView(id, title);
                        showMessage('Conversation selected');
                    });

                    const nameInput = div.querySelector('.prompt-name-input');
                    const editBtn = div.querySelector('.conversation-edit-btn');

                    const finishEdit = async (save) => {
                        if (!nameInput) return;
                        const original = nameInput.dataset.originalTitle || displayName;
                        const next = nameInput.value.trim();
                        nameInput.readOnly = true;
                        nameInput.style.borderColor = 'transparent';
                        nameInput.style.background = 'transparent';
                        if (!save || next === '' || next === original) {
                            nameInput.value = original;
                            nameInput.dataset.originalTitle = original;
                            return;
                        }
                        const ok = await updateConversationTitle(id, next, original);
                        if (!ok) {
                            nameInput.value = original;
                        } else {
                            nameInput.dataset.originalTitle = next;
                            if (activeConversationId === id) {
                                const promptTitle = document.getElementById('prompt-form-title');
                                if (promptTitle) promptTitle.textContent = next;
                            }
                        }
                    };

                    editBtn?.addEventListener('click', (e) => {
                        e.stopPropagation();
                        if (!nameInput) return;
                        nameInput.dataset.originalTitle = nameInput.value;
                        nameInput.readOnly = false;
                        nameInput.style.borderColor = '#0b63a6';
                        nameInput.style.background = '#ffffff';
                        nameInput.focus();
                        nameInput.select();
                    });

                    nameInput?.addEventListener('click', (e) => {
                        if (!nameInput.readOnly) e.stopPropagation();
                    });

                    nameInput?.addEventListener('keydown', async (e) => {
                        if (e.key === 'Enter') {
                            e.preventDefault();
                            await finishEdit(true);
                        } else if (e.key === 'Escape') {
                            e.preventDefault();
                            await finishEdit(false);
                        }
                    });

                    nameInput?.addEventListener('blur', async () => {
                        await finishEdit(true);
                    });

                    div.querySelector('.conversation-delete-btn')?.addEventListener('click', async (e) => {
                        e.stopPropagation();
                        await deleteConversation(id, title);
                    });

                    fragment.appendChild(div);
                } catch (err) {
                    console.error('Error creating sidebar item', err);
                }
            });
            if (list) list.appendChild(fragment);

            try {
                refreshConversationFileCounts(items);
            } catch (e) {
                console.warn('Failed to refresh conversation file counts', e);
            }

            state.prompts.hasMore = pagination.hasNext || pagination.HasNext || false;
            state.prompts.page++;
        } else if (reset && list) {
            list.innerHTML = `<div class="empty-sidebar">No conversations yet</div>`;
        }

        state.prompts.loading = false;
    } catch (error) {
        hideLoading('prompts-list');
        state.prompts.loading = false;
        console.error('Error loading prompts/conversations:', error);
        showMessage('Error loading items: ' + (error.message || error), 'error');
    }
}

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

        try {
            const response = await fetch(`${API_BASE}/files`, {
                method: 'POST',
                body: formData
            });

            if (response.ok) {
                showMessage('File uploaded successfully', 'success');
                mainFileInput.value = '';
                await loadFiles(true);
                await loadAllGroups(true);
            } else {
                let msg = 'Error uploading file';
                try { const error = await response.json(); msg = error.detail || msg; } catch (e){}
                showMessage(msg, 'error');
            }
        } catch (err) {
            showMessage('Error uploading file: ' + err.message, 'error');
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
            await loadAllGroups(true);
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error deleting file', 'error');
        }
    } catch (error) {
        showMessage('Error deleting file: ' + error.message, 'error');
    }
}

// Store current preview file info
let currentPreviewFile = { id: null, fileName: null };

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

        // PDF (detect by content-type or header)
        if (contentType.includes('pdf') || header === '%PDF') {
            const blobUrl = URL.createObjectURL(new Blob([buffer], { type: 'application/pdf' }));
            previewContent.dataset.blobUrl = blobUrl;
            previewContent.innerHTML = `<embed src="${blobUrl}" type="application/pdf" width="100%" height="400px" style="border-radius: 4px;">`;
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
        previewContent.innerHTML = '<p style="color: #e74c3c;">Failed to load file preview.</p>';
    }
}

function createPromptCard(prompt) {
    const div = document.createElement('div');
    div.className = 'sidebar-prompt-item';
    
    const id = getProp(prompt, 'id');
    const name = getProp(prompt, 'name') || 'Untitled';
    const promptText = getProp(prompt, 'promptText') || '';
    
    // Truncate name for sidebar
    const displayName = name.length > 25 ? name.substring(0, 25) + '...' : name;

    div.dataset.promptId = id;
    div.dataset.promptName = name;
    div.dataset.promptText = promptText;
    
    div.innerHTML = `
        <span class="prompt-name">${escapeHtml(displayName)}</span>
        <button type="button" class="prompt-delete-btn" title="Delete" aria-label="Delete">×</button>
    `;
    
    // Click to load prompt
    div.addEventListener('click', () => {
        // Don't auto-save when viewing a conversation (read-only operation)
        // Auto-save will happen via input, send, new_chat, or unload events
        document.querySelectorAll('.sidebar-prompt-item').forEach(item => item.classList.remove('active'));
        div.classList.add('active');
        loadPromptIntoForm(id, name, promptText);
    });

    // Delete prompt button
    div.querySelector('.prompt-delete-btn').addEventListener('click', (e) => {
        e.stopPropagation();
        deletePrompt(id);
    });
    
    return div;
}

// Auto Groups are view-only - no form submission needed
// Removed form submission handler as Auto Groups display conversation history and files

function loadPromptIntoForm(id, name, text) {
    document.getElementById('prompt-form-title').textContent = 'Conversation';
    
    // Parse conversation history and files from the prompt text
    // Assuming text is stored as JSON string with {messages: [], files: []}
    let messages = [];
    let files = [];
    try {
        const data = JSON.parse(text);
        messages = data.messages || [];
        files = data.files || [];
    } catch {
        // Fallback to old format
        messages = [
            {role: 'user', content: name},
            {role: 'bot', content: text}
        ];
    }
    
    // Show the conversation history in a scrollable box
    const conversationHTML = messages.map(msg => {
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
                    ${msgFiles.length > 0 ? `<button type="button" onclick="showAttachedFilesModal('${filesData}')" style="background: none; border: none; font-size: 16px; color: #0b63a6; cursor: pointer; padding: 0;" title="${escapeHtml(filesTitle)}">📎</button>` : ''}
                </div>
                <div style="color: #444; padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
                ${msg.role === 'bot' ? msgSourcesHtml : ''}
            </div>
        `;
    }).join('');
    
    // Show attached files in a scrollable box
    const normalizedFiles = files.map(f => {
        if (typeof f === 'string') {
            return { name: f };
        }
        return { name: f.name || f.fileName || f.FileName || 'Document', id: f.id || f.documentId };
    });

    const filesFromMessages = messages.flatMap(msg => {
        if (!Array.isArray(msg.files)) return [];
        return msg.files.map(f => {
            if (typeof f === 'string') {
                return { name: f };
            }
            return { name: f.name || f.fileName || f.FileName || 'Document', id: f.id || f.documentId };
        });
    });

    const mergedFiles = [...normalizedFiles, ...filesFromMessages].reduce((acc, file) => {
        const key = `${(file.name || '').toLowerCase()}::${file.id || ''}`;
        if (!acc.seen.has(key)) {
            acc.seen.add(key);
            acc.items.push(file);
        }
        return acc;
    }, { seen: new Set(), items: [] }).items;

    const filesHTML = mergedFiles.length > 0 ? `
        <div style="margin-top: 15px; padding: 15px; background: #f8f9fa; border-radius: 8px;">
            <div style="font-weight: 600; color: #2c3e50; margin-bottom: 10px;">Attached Files</div>
            <div style="max-height: 150px; overflow-y: auto; border: 1px solid #e0e0e0; border-radius: 4px; padding: 10px; background: white;">
                ${mergedFiles.map(f => {
                    const fileId = f.id || '';
                    const fileName = f.name || 'Document';
                    return `
                        <div style="padding: 4px 0; color: #444; display: flex; align-items: center; justify-content: space-between; gap: 8px;">
                            <span>📎 ${escapeHtml(fileName)}</span>
                            <button type="button" onclick="previewDocumentFile('${fileId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')" style="padding: 2px 8px; background: #0b63a6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;">Preview</button>
                        </div>
                    `;
                }).join('')}
            </div>
        </div>
    ` : '';
    
    const responseContent = document.getElementById('response-content');
    responseContent.innerHTML = `
        <div style="margin-bottom: 15px; padding-bottom: 10px; border-bottom: 1px solid #e0e0e0; display: flex; align-items: center; justify-content: space-between;">
            <div style="color: #2c3e50; font-weight: 600;">Auto Group: ${escapeHtml(name)}</div>
            <button type="button" onclick="closeAutoGroupView()" style="background: none; border: none; font-size: 20px; color: #95a5a6; cursor: pointer; padding: 0; line-height: 1;">×</button>
        </div>
        <div style="max-height: 400px; overflow-y: auto; padding: 10px; border: 1px solid #e0e0e0; border-radius: 4px; background: #fafafa;">
            ${conversationHTML}
        </div>
        ${filesHTML}
    `;
}

function createNewPrompt() {
    document.querySelectorAll('.sidebar-prompt-item').forEach(item => item.classList.remove('active'));
    document.getElementById('prompt-form-title').textContent = 'Conversation';
    
    // Reset conversation history
    currentConversationHistory = [];
    activePromptGroupId = null;
    updateActiveGroupStyles();
    
    // Reset response section
    const responseContent = document.getElementById('response-content');
    if (responseContent) {
        responseContent.innerHTML = '<span style="color: #2f3a3f;">Enter a message and click Send to get a response</span>';
    }
    
    // Clear input
    const promptInput = document.getElementById('prompt-name');
    if (promptInput) {
        promptInput.value = '';
    }
}

async function loadConversationIntoView(conversationId, title) {
    if (!conversationId) return;
    try {
        await joinConversationHubGroup(conversationId);
        const responseContent = document.getElementById('response-content');
        if (responseContent) {
            responseContent.innerHTML = '<span style="color: #2f3a3f;">Loading conversation...</span>';
        }

        const res = await fetchWithRetry(`/api/conversations/${conversationId}`);
        if (!res.ok) {
            throw new Error(`HTTP ${res.status}: ${res.statusText}`);
        }

        const conversation = await res.json().catch(() => null);
        const messages = (conversation && (conversation.messages || conversation.Messages)) || [];

        currentConversationHistory = Array.isArray(messages)
            ? messages.map(m => {
                const rawFiles = Array.isArray(m.files)
                    ? m.files
                    : (Array.isArray(m.Files)
                        ? m.Files
                        : (Array.isArray(m.Attachments)
                            ? m.Attachments
                            : (Array.isArray(m.attachments) ? m.attachments : [])));

                return {
                    role: (m.role || m.Role || '').toLowerCase() === 'assistant' ? 'bot' : (m.role || m.Role || 'user').toLowerCase(),
                    content: m.content || m.Content || '',
                    files: normalizeFileList(rawFiles),
                    sources: Array.isArray(m.sources) ? m.sources : (Array.isArray(m.Sources) ? m.Sources : [])
                };
            })
            : [];

        const promptIdInput = document.getElementById('prompt-id');
        if (promptIdInput) promptIdInput.value = '';
        const promptTitle = document.getElementById('prompt-form-title');
        if (promptTitle) promptTitle.textContent = title || 'Conversation';

        if (currentConversationHistory.length === 0) {
            if (responseContent) {
                responseContent.innerHTML = '<span style="color: #2f3a3f;">No messages yet</span>';
            }
            return;
        }

        const conversationHTML = currentConversationHistory.map((msg, index) => {
            const closeBtn = (index === 0 && msg.role === 'user')
                ? `<button type="button" onclick="closeAutoGroupView()" title="Close" aria-label="Close" style="border:none;background:#95a5a6;color:white;width:20px;height:20px;border-radius:50%;cursor:pointer;font-size:14px;line-height:20px;padding:0;">×</button>`
                : '';

            return `
                <div style="margin-bottom: 12px;">
                    <div style="color: ${msg.role === 'user' ? '#0b63a6' : '#27ae60'}; font-weight: 600; margin-bottom: 4px; display: flex; align-items: center; gap: 8px;">
                        <span>${msg.role === 'user' ? 'User' : 'Bot'}:</span>
                        ${closeBtn}
                    </div>
                    <div style="color: #444; padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
                </div>
            `;
        }).join('');


        // Build list of files used in this conversation (attachments + sources)
        const filesFromMessages = currentConversationHistory.flatMap(msg => {
            const list = [];
            normalizeFileList(msg.files).forEach(f => list.push(f));
            if (Array.isArray(msg.sources)) {
                msg.sources.forEach(s => {
                    if (!s) return;
                    const name = s.fileName || s.FileName || s.name || 'Document';
                    const id = s.documentId || s.DocumentId || s.id;
                    const page = s.pageNumber || s.PageNumber;
                    list.push({ name: page ? `${name} (p.${page})` : name, id });
                });
            }
            return list;
        });

        const mergedFiles = filesFromMessages.reduce((acc, file) => {
            const key = `${(file.name || '').toLowerCase()}::${file.id || ''}`;
            if (!acc.seen.has(key)) {
                acc.seen.add(key);
                acc.items.push(file);
            }
            return acc;
        }, { seen: new Set(), items: [] }).items;

        const filesHTML = mergedFiles.length > 0 ? `
            <div style="margin-top: 12px; padding: 12px; background: #f8f9fa; border-radius: 8px;">
                <div style="font-weight: 600; color: #2c3e50; margin-bottom: 8px;">Files used in this conversation</div>
                <div style="max-height: 200px; overflow-y: auto; border: 1px solid #e0e0e0; border-radius: 4px; padding: 8px; background: white;">
                    ${mergedFiles.map(f => {
                        const fileId = f.id || '';
                        const fileName = f.name || 'Document';
                        return `
                            <div style="padding: 6px 0; display:flex; align-items:center; justify-content:space-between; gap:8px;">
                                <span>📎 ${escapeHtml(fileName)}</span>
                                <button type="button" onclick="previewDocumentFile('${fileId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')" style="padding:2px 8px; background:#0b63a6; color:white; border:none; border-radius:4px; cursor:pointer; font-size:12px;">Preview</button>
                            </div>
                        `;
                    }).join('')}
                </div>
            </div>
        ` : '';

        if (responseContent) {
            responseContent.innerHTML = `
                <div style="max-height: 500px; overflow-y: auto; padding: 15px; background: #f8f9fa; border-radius: 8px;">
                    ${conversationHTML}
                </div>
                ${filesHTML}
            `;
        }
    } catch (error) {
        console.error('[CONVERSATION] Failed to load conversation:', error);
        showMessage('Failed to load conversation: ' + error.message, 'error');
    }
}

// Handle file attachments for prompts
let promptPreviewFile = null;
let promptSelectedFiles = [];
let reminderSelectedFiles = []; // Reminder file attachments (separate from prompts)
let currentConversationHistory = []; // Track conversation messages
let activePromptGroupId = null;
// conversation mapping is now server-side; frontend will not persist ConversationId
let activeConversationId = null;
window.promptSelectedFiles = promptSelectedFiles;
window.reminderSelectedFiles = reminderSelectedFiles;
let lastAutoSavedSnapshot = null;
let autoSaveInFlight = false;
const draftStorageKey = 'adminPromptDraft';
// Toggle automatic draft saving/restoring in the browser (false = disabled)
// NOTE: enable to restore autosave for groups and drafts
const AUTO_SAVE_ENABLED = true;

function buildConversationSnapshot() {
    const files = promptSelectedFiles.map(f => ({
        name: f.name || f.file?.name || 'Document',
        id: f.documentId || f.id
    }));

    return {
        messages: currentConversationHistory,
        files: files
    };
}

function hasConversationContent() {
    return currentConversationHistory.length > 0 || promptSelectedFiles.length > 0;
}

async function autoSaveCurrentConversation(reason) {
    if (!AUTO_SAVE_ENABLED || autoSaveInFlight || !hasConversationContent()) return null;

    const snapshot = buildConversationSnapshot();
    const snapshotText = JSON.stringify(snapshot);
    if (snapshotText === lastAutoSavedSnapshot) return null;

    // Don't auto-save if there are no messages (only files is not enough to save)
    if (snapshot.messages.length === 0) {
        console.log('[AUTO SAVE] Skipping save - no messages in conversation');
        return null;
    }

    autoSaveInFlight = true;
    try {
        const firstMessage = snapshot.messages.find(m => m.role === 'user');
        // If no user message exists, skip auto-save to prevent "Untitled Chat"
        if (!firstMessage) {
            console.log('[AUTO SAVE] Skipping save - no user message found');
            autoSaveInFlight = false;
            return null;
        }
        const chatName = firstMessage.content.substring(0, 50);
        const payload = { name: chatName, promptText: snapshotText };

        const existingId = document.getElementById('prompt-id')?.value;
        const url = existingId ? `${API_BASE}/prompts/${existingId}` : `${API_BASE}/prompts`;
        const method = existingId ? 'PUT' : 'POST';

        const response = await fetch(url, {
            method,
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            console.warn('[AUTO SAVE] Failed to save conversation to server:', response.status);
            return null;
        }

        const result = await response.json().catch(() => null);
        const savedId = result?.id || result?.Id || null;
        if (savedId) {
            const promptIdInput = document.getElementById('prompt-id');
            if (promptIdInput) promptIdInput.value = savedId;

            // Fetch canonical saved record from server and load into UI
            try {
                // Try direct GET by id first (some APIs may not expose GET /prompts/{id})
                let savedPrompt = null;
                let savedRes = await fetchWithRetry(`${API_BASE}/prompts/${savedId}`);
                if (savedRes.ok) {
                    const savedJson = await savedRes.json().catch(() => null);
                    savedPrompt = (savedJson && (savedJson.data || savedJson)) || savedJson || null;
                } else if (savedRes.status === 405 || savedRes.status === 404) {
                    // Fallback: fetch the prompts list and find the saved item
                    const listRes = await fetchWithRetry(`${API_BASE}/prompts?page=1&pageSize=1000`);
                    if (listRes.ok) {
                        const listJson = await listRes.json().catch(() => null);
                        const prompts = (listJson && (listJson.data || listJson.Data)) || listJson || [];
                        savedPrompt = prompts.find(p => (p.id === savedId || p.Id === savedId || p.Id === (savedId + '')) ) || null;
                    }
                }

                if (savedPrompt) {
                    const promptText = savedPrompt.PromptText || savedPrompt.promptText || '';
                    try {
                        loadPromptIntoForm(savedId, savedPrompt.name || chatName, promptText);
                    } catch (e) {
                        console.warn('[AUTO SAVE] loadPromptIntoForm failed:', e);
                    }

                    // Refresh sidebar lists so other clients / tabs see the new entry
                    try {
                        await loadPrompts(true);
                        await loadAllGroups(true);
                    } catch (e) {
                        console.warn('[AUTO SAVE] Failed to refresh lists:', e);
                    }
                } else {
                    console.warn('[AUTO SAVE] Canonical prompt not found after save');
                }
            } catch (fetchErr) {
                console.warn('[AUTO SAVE] Error fetching canonical prompt:', fetchErr);
            }
        }

        lastAutoSavedSnapshot = snapshotText;
        console.log(`[AUTO SAVE] Conversation saved to server (${reason})`);
        return savedId;
    } catch (error) {
        console.warn('[AUTO SAVE] Error saving conversation to server:', error);
        return null;
    } finally {
        autoSaveInFlight = false;
    }
}

function restoreDraftConversation() {
    // Browser storage draft restoration removed.
    return false;
}

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
            return `
                <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0; ${fileObj.selected ? 'background: #f0f0f0;' : ''}">
                    <input type="checkbox" ${fileObj.selected ? 'checked' : ''} style="width: 16px; height: 16px; cursor: pointer; margin: 0;" onclick="togglePromptFileSelection(${index})">
                    <span onclick="togglePromptFileSelection(${index})" style="font-size: 14px; color: #333; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; user-select: none;">
                        ${escapeHtml(fileName)}
                    </span>
                    <button type="button" onclick="event.stopPropagation(); previewDocumentFile('${fileId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')" style="padding: 4px 10px; background: #0b63a6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;">
                        Preview
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
                        loadGroups(true).catch(() => {});
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
    window.promptSelectedFiles = promptSelectedFiles;
    updatePromptFilesList();
};


function updateActiveGroupStyles() {
    document.querySelectorAll('[data-group-card="true"]').forEach(card => {
        const groupId = card.dataset.groupId;
        const isActive = !!activePromptGroupId && groupId === String(activePromptGroupId);
        const badge = card.querySelector('[data-group-active-badge="true"]');
        const input = card.querySelector('input[data-group-id]');

        card.style.background = isActive ? '#eaf4ff' : '#f5f5f5';
        card.style.border = isActive ? '2px solid #0b63a6' : '2px solid transparent';
        if (input) {
            input.style.background = isActive ? '#ffffff' : 'white';
            input.style.borderColor = isActive ? '#0b63a6' : '#ddd';
        }
        if (badge) {
            badge.style.display = isActive ? 'inline-flex' : 'none';
        }
    });
}

function getConversationGroupKey() {
    return activePromptGroupId ? String(activePromptGroupId) : 'ungrouped';
}

function setActivePromptGroup(groupId, groupName) {
    activePromptGroupId = groupId || null;
    updateActiveGroupStyles();
    if (groupName) {
        showMessage(`Active group set to: ${groupName}`);
    }
}

async function syncActiveGroupFromConversation(conversationId) {
    if (!conversationId) return;
    try {
        const res = await fetchWithRetry(`/api/conversations/${conversationId}`);
        if (!res.ok) return;
        const conversation = await res.json().catch(() => null);
        if (!conversation) return;

        const groupId = getProp(conversation, 'resourceGroupId')
            || getProp(conversation, 'ResourceGroupId')
            || getProp(getProp(conversation, 'resourceGroup'), 'id')
            || getProp(getProp(conversation, 'ResourceGroup'), 'id');
        const groupName = getProp(getProp(conversation, 'resourceGroup'), 'name')
            || getProp(getProp(conversation, 'ResourceGroup'), 'name');

        if (groupId) {
            activePromptGroupId = groupId;
            updateActiveGroupStyles();
            await loadGroups(true);
            await loadAllGroups(true);
        }
    } catch (error) {
        console.warn('[AUTO GROUP] Failed to sync active group:', error);
    }
}

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
            await loadAllGroups(true);
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
                activeConversationId = null;
                closeAutoGroupView();
            }
            removeConversationFromUI(id);
            showMessage('Conversation deleted successfully');
            apiCache.invalidate('/api/conversations');
            apiCache.invalidate('/api/admin/groups');
            await loadPrompts(true);
            await loadGroups(true);
            await loadAllGroups(true);
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
window.createNewPrompt = createNewPrompt;
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
async function loadGroups(reset = false) {
    if (state.groups.loading) return;
    if (!reset && !state.groups.hasMore) return;
    
    if (reset) {
        state.groups.page = 1;
        state.groups.hasMore = true;
        state.groups.data = [];
        document.getElementById('groups-list').innerHTML = '';
    }
    
    state.groups.loading = true;
    showLoading('groups-list');
    
    try {
        const cacheBust = reset ? `&cb=${Date.now()}` : '';
        const response = await fetchWithRetry(`${API_BASE}/groups?page=${state.groups.page}&pageSize=${state.groups.pageSize}${cacheBust}`);
        
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        
        const result = await response.json();
        console.log('Groups API response:', result);
        // Handle both lowercase and uppercase from API
        const groupsData = result.data || result.Data || [];
        const pagination = result.pagination || result.Pagination || {};
        console.log('Groups data length:', groupsData.length);
        
        const list = document.getElementById('groups-list');
        
        hideLoading('groups-list');
        
        if (groupsData && groupsData.length > 0) {
            state.groups.data.push(...groupsData);
            
            const fragment = document.createDocumentFragment();
            groupsData.forEach(group => {
                const div = createGroupCard(group);
                // Skip null returns (reminders are filtered out)
                if (div) {
                    fragment.appendChild(div);
                }
            });
            list.appendChild(fragment);
            
            state.groups.hasMore = pagination.hasNext || pagination.HasNext;
            state.groups.page++;
        } else if (reset) {
            list.innerHTML = '<div class="empty-state"><p>No groups created yet</p></div>';
        }
        
        state.groups.loading = false;
    } catch (error) {
        hideLoading('groups-list');
        state.groups.loading = false;
        showMessage('Error loading groups: ' + error.message, 'error');
    }
}

async function loadReminders(reset = false) {
    if (state.reminders.loading) return;
    if (!reset && !state.reminders.hasMore) return;
    
    if (reset) {
        state.reminders.page = 1;
        state.reminders.hasMore = true;
        state.reminders.data = [];
        document.getElementById('reminders-list').innerHTML = '';
    }
    
    state.reminders.loading = true;
    showLoading('reminders-list');
    
    try {
        // Load all groups and filter for those with description "Reminder"
        const response = await fetchWithRetry(`${API_BASE}/groups?page=${state.reminders.page}&pageSize=${state.reminders.pageSize}`);
        
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        
        const result = await response.json();
        const groupsData = result.data || result.Data || [];
        const pagination = result.pagination || result.Pagination || {};
        
        // Filter for reminders (groups with description "Reminder" - case insensitive)
        const remindersData = groupsData.filter(g => {
            const desc = getProp(g, 'description') || getProp(g, 'Description') || '';
            return desc.toLowerCase() === 'reminder';
        });
        
        // NOTE: Reminders can exist without files, so we don't auto-delete empty reminders
        // This is different from groups which require at least one resource
        
        const list = document.getElementById('reminders-list');
        hideLoading('reminders-list');
        
        if (remindersData && remindersData.length > 0) {
            state.reminders.data.push(...remindersData);
            
            const fragment = document.createDocumentFragment();
            remindersData.forEach(reminder => {
                const div = createReminderCard(reminder);
                fragment.appendChild(div);
            });
            list.appendChild(fragment);
            
            state.reminders.hasMore = pagination.hasNext || pagination.HasNext;
            state.reminders.page++;
        } else if (reset) {
            list.innerHTML = '<div class="empty-state"><p>No reminders created yet</p></div>';
        }
        
        state.reminders.loading = false;
    } catch (error) {
        hideLoading('reminders-list');
        state.reminders.loading = false;
        showMessage('Error loading reminders: ' + error.message, 'error');
    }
}

function createReminderCard(reminder) {
    const div = document.createElement('div');
    const id = getProp(reminder, 'id');
    const name = getProp(reminder, 'name') || 'Untitled';
    
    const files = getProp(reminder, 'files') || [];
    const documents = getProp(reminder, 'documents') || getProp(reminder, 'Documents') || [];
    const fileCount = (Array.isArray(files) ? files.length : 0) + (Array.isArray(documents) ? documents.length : 0);

    div.dataset.reminderId = id;
    div.style.cssText = 'background: #f5f5f5; padding: 12px; border-radius: 6px; margin-bottom: 10px; position: relative; border: 2px solid transparent;';
    
    div.innerHTML = `
        <div style="display: flex; gap: 10px; align-items: center; margin-bottom: 8px;">
            <div style="position: relative; flex: 1;">
                <input type="text" value="${escapeHtml(name)}" readonly data-reminder-id="${id}" style="width: 100%; padding: 12px 50px 12px 12px; border: 1px solid #ddd; border-radius: 4px; font-size: 14px; background: white; cursor: pointer;">
                <span class="reminder-item-paperclip" data-reminder-id="${id}" data-reminder-name="${escapeHtml(name)}" style="position: absolute; right: 12px; top: 50%; transform: translateY(-50%); font-size: 24px; color: #95a5a6; cursor: pointer;">📎</span>
            </div>
            <button type="button" class="reminder-item-delete" data-reminder-id="${id}" style="padding: 8px 16px; background: #e74c3c; color: white; border: none; border-radius: 4px; cursor: pointer; white-space: nowrap; font-size: 14px;">Delete</button>
        </div>
        <div style="display: flex; gap: 8px; padding-left: 12px; font-size: 12px; color: #666;">
            <span title="${fileCount} file(s)"><span aria-hidden="true">📄</span> <span aria-label="${fileCount} files">${fileCount} files</span></span>
        </div>
    `;

    // Attach event listener to delete button
    div.querySelector('.reminder-item-delete').addEventListener('click', async (e) => {
        if (!confirm('Are you sure you want to delete this reminder?')) return;
        try {
            const response = await fetch(`${API_BASE}/groups/${id}`, { method: 'DELETE' });
            if (response.ok) {
                showMessage('Reminder deleted successfully');
                await loadReminders(true);
            } else {
                showMessage('Error deleting reminder', 'error');
            }
        } catch (error) {
            showMessage('Error deleting reminder: ' + error.message, 'error');
        }
    });

    // Attach event listener to paperclip to open preview modal
    div.querySelector('.reminder-item-paperclip').addEventListener('click', () => {
        manageGroupResources(id, name);
    });
    
    return div;
}

function createGroupCard(group) {
    const div = document.createElement('div');
    const id = getProp(group, 'id');
    const rawName = getProp(group, 'name') || '';
    const name = String(rawName).trim() || 'Untitled';
    const description = getProp(group, 'description') || getProp(group, 'Description') || '';
    const isActive = !!activePromptGroupId && String(activePromptGroupId) === String(id);
    
    // Skip if this is a reminder (will be shown in Reminders tab)
    if (description && description.toLowerCase() === 'reminder') {
        return null;
    }
    
    // Get counts for files, conversations, and reminders
    const files = getProp(group, 'files') || [];
    const documents = getProp(group, 'documents') || getProp(group, 'Documents') || [];
    const conversations = getProp(group, 'conversations') || [];
    const reminders = getProp(group, 'reminders') || [];
    const fileCount = (Array.isArray(files) ? files.length : 0) + (Array.isArray(documents) ? documents.length : 0);
    const conversationCount = Array.isArray(conversations) ? conversations.length : 0;
    const reminderCount = Array.isArray(reminders) ? reminders.length : 0;

    div.dataset.groupCard = 'true';
    div.dataset.groupId = id;
    div.className = `group-card ${isActive ? 'group-card-active' : ''}`;
    
    div.innerHTML = `
        <div style="display: flex; gap: 10px; align-items: center; margin-bottom: 8px;">
            <div style="position: relative; flex: 1;">
                <input type="text" value="${escapeHtml(name)}" readonly data-group-id="${id}" class="group-card-input ${isActive ? 'group-card-input-active' : ''}" style="width: 100%; padding: 12px 90px 12px 12px; border-radius: 4px; font-size: 14px; cursor: pointer;">
                <span data-group-active-badge="true" class="group-badge group-badge-active" style="display: ${isActive ? 'inline-flex' : 'none'};">Active</span>
                <span class="group-item-paperclip" data-group-id="${id}" data-group-name="${escapeHtml(name)}" style="position: absolute; right: 12px; top: 50%; transform: translateY(-50%); font-size: 24px; color: #95a5a6; cursor: pointer;">📎</span>
            </div>
            <button type="button" class="group-item-delete" data-group-id="${id}" style="padding: 8px 16px; background: #e74c3c; color: white; border: none; border-radius: 4px; cursor: pointer; white-space: nowrap; font-size: 14px;">Delete</button>
        </div>
        <div class="group-card-meta" style="display: flex; gap: 8px; padding-left: 12px; font-size: 12px;">
            <span title="${fileCount} file(s)" data-group-file-count-title="${id}"><span aria-hidden="true">📄</span> <span aria-label="${fileCount} files" data-group-file-count="${id}">${fileCount} files</span></span>
            <span title="${conversationCount} conversation(s)"><span aria-hidden="true">🗨️</span> <span aria-label="${conversationCount} conversations">${conversationCount} conversations</span></span>
            <span title="${reminderCount} reminder(s)"><span aria-hidden="true">⏰</span> <span aria-label="${reminderCount} reminders">${reminderCount} reminders</span></span>
        </div>
    `;

    // Click group name to set active prompt group and show contents
    const input = div.querySelector('input[data-group-id]');
    input?.addEventListener('click', () => {
        setActivePromptGroup(id, name);
        manageGroupResources(id, name);
    });
    
    // Attach event listener to paperclip
    div.querySelector('.group-item-paperclip').addEventListener('click', (e) => {
        manageGroupResources(e.target.dataset.groupId, e.target.dataset.groupName);
    });
    
    // Attach event listener to delete button
    div.querySelector('.group-item-delete').addEventListener('click', (e) => {
        deleteGroup(e.target.dataset.groupId);
    });
    
    return div;
}

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
                window.groupSelectedFiles = groupSelectedFiles;
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
            await loadGroups(true);
            await loadAllGroups(true); // Force refresh groups cache
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error saving reminder', 'error');
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
            const error = await response.json();
            showMessage(error.detail || 'Error saving reminder', 'error');
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

function editGroup(id, name, description) {
    isEditingGroup = true;
    document.getElementById('group-id').value = id;
    document.getElementById('group-name').value = name;
    document.getElementById('group-description').value = description;
    document.getElementById('group-submit-btn').textContent = 'Update Group';
    document.getElementById('group-cancel-btn').style.display = 'inline-block';
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function cancelEditGroup() {
    isEditingGroup = false;
    document.getElementById('group-form').reset();
    document.getElementById('group-id').value = '';
    document.getElementById('group-submit-btn').textContent = 'Create Group';
    document.getElementById('group-cancel-btn').style.display = 'none';
}

document.getElementById('group-cancel-btn').addEventListener('click', cancelEditGroup);

async function deleteGroup(id) {
    if (!confirm('Are you sure you want to permanently delete this group and all its conversations? This cannot be undone.')) return;

    try {
        // 1) Fetch all conversations for this group (includes active conversations)
        const convRes = await fetch(`/api/conversations?groupId=${encodeURIComponent(id)}`);
        if (!convRes.ok) {
            showMessage('Failed to load conversations for group', 'error');
            return;
        }

        const convJson = await convRes.json();
        const conversations = Array.isArray(convJson)
            ? convJson
            : (convJson?.data || convJson?.Data || []);

        // 2) Permanently delete each conversation via the permanent delete API
        if (conversations.length > 0) {
            showMessage(`Deleting ${conversations.length} conversations...`);
            const deletePromises = conversations
                .map(c => c?.id || c?.Id)
                .filter(Boolean)
                .map(conversationId =>
                    fetch(`/api/conversations/${conversationId}`, { method: 'DELETE' })
                        .then(r => ({ ok: r.ok, status: r.status, id: conversationId }))
                        .catch(e => ({ ok: false, error: e.message, id: conversationId }))
                );

            const results = await Promise.all(deletePromises);
            const failed = results.filter(r => !r.ok);
            if (failed.length > 0) {
                console.warn('Failed to delete some conversations', failed);
                showMessage(`Failed to delete ${failed.length} conversations. Aborting group delete.`, 'error');
                return;
            }
        }

        // 3) Delete the group itself via admin API
        const response = await fetch(`${API_BASE}/groups/${id}`, { method: 'DELETE' });
        if (response.ok || response.status === 204) {
            showMessage('Group and its conversations deleted successfully');
            if (activePromptGroupId === id) {
                activePromptGroupId = null;
            }
            await loadGroups(true);
            await loadAllGroups(true); // Force refresh groups cache
        } else {
            const error = await response.json().catch(() => null);
            showMessage(error?.detail || 'Error deleting group', 'error');
        }
    } catch (error) {
        showMessage('Error deleting group: ' + (error?.message || error), 'error');
    }
}

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
            const listRes = await fetchWithRetry(`${API_BASE}/prompts?page=1&pageSize=1000`);
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
async function manageGroupResources(groupId, groupName) {
    try {
        console.log('[MANAGE] Looking for group ID:', groupId);
        
        // ALWAYS fetch fresh group data to avoid stale cache
        console.log('[MANAGE] Fetching fresh group data...');
        const groupRes = await fetchWithRetry(`${API_BASE}/groups?page=1&pageSize=1000`);
        if (!groupRes.ok) {
            throw new Error(`Failed to fetch groups: ${groupRes.status}`);
        }
        const groupsResult = await groupRes.json();
        // Handle both lowercase and uppercase from API
        const groups = groupsResult.data || groupsResult.Data || [];
        console.log('[MANAGE] Groups fetched:', groups.length);
        
        // Find group using case-insensitive property access
        const currentGroup = groups.find(g => getProp(g, 'id') === groupId);
        
        if (!currentGroup) {
            console.error('[MANAGE] Group not found. Looking for:', groupId);
            console.error('[MANAGE] Available groups:', groups.map(g => getProp(g, 'id')));
            throw new Error('Group not found');
        }
        
        console.log('[MANAGE] Using fresh group data:', currentGroup);
        
        // ALWAYS fetch files and prompts fresh - don't rely on state cache
        let allFiles = [];
        let allPrompts = [];
        
        try {
            console.log('[MANAGE] Fetching batch data for manage resources...');
            
            // Use new batch endpoint - single call instead of 2 separate calls
            const batchRes = await fetchWithRetry(`${API_BASE}/batch?includeFiles=true&includePrompts=true&includeGroups=false&maxItems=1000`);
            
            console.log('[MANAGE] Batch response status:', batchRes.status);
            
            if (batchRes.ok) {
                const batchResult = await batchRes.json();
                console.log('[MANAGE] Batch response:', batchResult);
                
                // Handle files
                if (batchResult.files) {
                    allFiles = batchResult.files.data || batchResult.files.Data || [];
                    console.log('[MANAGE] Fetched files count:', allFiles.length);
                }
                
                // Handle prompts
                if (batchResult.prompts) {
                    allPrompts = batchResult.prompts.data || batchResult.prompts.Data || [];
                    console.log('[MANAGE] Fetched prompts count:', allPrompts.length);
                }
                
                if (allFiles.length > 0) {
                    console.log('[MANAGE] Sample file:', allFiles[0]);
                }
                if (allPrompts.length > 0) {
                    console.log('[MANAGE] Sample prompt:', allPrompts[0]);
                }
            } else {
                console.error('[MANAGE] Batch request failed:', batchRes.status);
                showMessage('Failed to load resources', 'error');
                return;
            }
        } catch (fetchError) {
            console.error('[MANAGE] Error fetching resources:', fetchError);
            showMessage('Error loading resources: ' + fetchError.message, 'error');
            return;
        }
        
        console.log('[MANAGE] Final counts - Files:', allFiles.length, 'Prompts:', allPrompts.length);
        
        if (allFiles.length === 0 && allPrompts.length === 0) {
            console.warn('[MANAGE] WARNING: No files or prompts returned from API');
        }
        
        // Use getProp to access files and prompts arrays with case-insensitive fallback
        const groupFiles = getProp(currentGroup, 'files') || [];
        const groupDocuments = getProp(currentGroup, 'documents') || getProp(currentGroup, 'Documents') || [];
        const groupPrompts = getProp(currentGroup, 'prompts') || [];
        const groupConversations = getProp(currentGroup, 'conversations') || [];
        const groupReminders = getProp(currentGroup, 'reminders') || [];
        const groupFileIds = new Set(groupFiles.map(f => getProp(f, 'id')));
        const groupDocumentIds = new Set(groupDocuments.map(d => getProp(d, 'id')));
        const groupPromptIds = new Set(groupPrompts.map(p => getProp(p, 'id')));
        
        console.log('[MANAGE] Group files:', groupFiles);
        console.log('[MANAGE] Group conversations:', groupConversations);
        console.log('[MANAGE] Group reminders:', groupReminders);
        
        const modalBody = document.getElementById('modal-content-body');

        // Build files list HTML - include files attached to the group AND files referenced by conversations in the group
        let filesListHTML = '';

        // Gather files referenced by conversations under this group
        let convReferencedFiles = [];
        try {
            console.log('[MANAGE] Fetching conversations for group to collect referenced files...');
            const convsRes = await fetchWithRetry(`/api/conversations?groupId=${groupId}&page=1&pageSize=1000`);
            if (convsRes.ok) {
                const convsJson = await convsRes.json().catch(() => null);
                const convs = convsJson?.data || convsJson || [];
                console.log('[MANAGE] Conversations fetched for file extraction:', convs.length);
                // For each conversation, try to load messages (if provided inline) or fetch details
                for (const c of convs) {
                    const convoId = getProp(c, 'id');
                    let messages = null;
                    try {
                        const det = await fetchWithRetry(`/api/conversations/${convoId}`);
                        if (det.ok) {
                            const detJson = await det.json().catch(() => null);
                            messages = detJson?.messages || detJson?.Messages || detJson || [];
                        }
                    } catch (e) {
                        console.warn('[MANAGE] Failed to fetch conversation details for', convoId, e);
                    }

                    if (Array.isArray(messages)) {
                        messages.forEach(m => {
                            const msgFiles = Array.isArray(m.files)
                                ? m.files
                                : (Array.isArray(m.Attachments)
                                    ? m.Attachments
                                    : (Array.isArray(m.attachments) ? m.attachments : []));

                            msgFiles.forEach(f => {
                                if (!f) return;
                                if (typeof f === 'string') {
                                    convReferencedFiles.push({ name: f });
                                } else {
                                    const name = f.name || f.fileName || f.FileName || f.OriginalFileName || 'Document';
                                    const id = f.id || f.documentId || f.DocumentId || f.DocumentMetadataId;
                                    convReferencedFiles.push({ name, id });
                                }
                            });

                            if (Array.isArray(m.sources)) {
                                m.sources.forEach(s => {
                                    if (!s) return;
                                    const name = s.fileName || s.FileName || s.name || 'Document';
                                    const id = s.documentId || s.DocumentId || s.id;
                                    const page = s.pageNumber || s.PageNumber;
                                    convReferencedFiles.push({ name: page ? `${name} (p.${page})` : name, id });
                                });
                            }
                        });
                    }
                }
            } else {
                console.warn('[MANAGE] Failed to fetch conversations list for group file extraction', convsRes.status);
            }
        } catch (err) {
            console.warn('[MANAGE] Error fetching convs for files:', err);
        }

        // Merge groupFiles with convReferencedFiles, dedupe by name+id
        const combinedFiles = [];
        const seen = new Set();
        const pushFile = (f) => {
            const name = getProp(f, 'originalFileName') || getProp(f, 'fileName') || f.name || 'Document';
            const id = getProp(f, 'id') || f.id || f.documentId || '';
            const isDocument = !!f.isDocument;
            const key = `${String(name).toLowerCase()}::${id}::${isDocument ? 'doc' : 'file'}`;
            if (!seen.has(key)) {
                seen.add(key);
                combinedFiles.push({ name, id, isDocument });
            }
        };

        (groupFiles || []).forEach(f => pushFile({
            id: getProp(f, 'id'),
            name: getProp(f, 'originalFileName') || getProp(f, 'fileName') || 'Document',
            isDocument: false
        }));

        (groupDocuments || []).forEach(d => pushFile({
            id: getProp(d, 'id'),
            name: getProp(d, 'originalFileName') || getProp(d, 'fileName') || 'Document',
            isDocument: true
        }));

        convReferencedFiles.forEach(f => pushFile({
            id: f.id || f.documentId || '',
            name: f.name || 'Document',
            isDocument: true
        }));

        // Sync group card file count with the modal's combined list
        const groupIdKey = String(groupId);
        const countEl = document.querySelector(`[data-group-file-count="${groupIdKey}"]`);
        const countTitleEl = document.querySelector(`[data-group-file-count-title="${groupIdKey}"]`);
        if (countEl) {
            countEl.textContent = `${combinedFiles.length} files`;
            countEl.setAttribute('aria-label', `${combinedFiles.length} files`);
        }
        if (countTitleEl) {
            countTitleEl.setAttribute('title', `${combinedFiles.length} file(s)`);
        }

        if (combinedFiles.length === 0) {
            filesListHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No attachments yet</div>';
        } else {
            filesListHTML = combinedFiles.map(file => {
                const fileName = file.name || 'Unknown';
                const fileId = file.id || '';
                const previewFn = file.isDocument ? 'previewDocumentFile' : 'previewReminderFile';
                return `
                    <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0;">
                        <span style="font-size: 14px; color: #333; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                            ${escapeHtml(String(fileName))}
                        </span>
                        <button type="button" onclick="${previewFn}('${fileId}', '${escapeHtml(String(fileName)).replace(/'/g, "\\'")}')"
                            style="padding: 4px 12px; background: #0b63a6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; white-space: nowrap;">
                            Preview
                        </button>
                    </div>
                `;
            }).join('');
        }
        
        // Build conversations list HTML
        let conversationsListHTML = '';
        if (groupConversations.length === 0) {
            conversationsListHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No conversations yet</div>';
        } else {
            conversationsListHTML = groupConversations.map(convo => {
                const convoId = getProp(convo, 'id');
                const convoTitle = getProp(convo, 'title') || 'Untitled';
                const messageCount = getProp(convo, 'messageCount') || 0;
                const isActive = getProp(convo, 'isActive') || false;
                const statusColor = isActive ? '#27ae60' : '#95a5a6';
                const statusText = isActive ? 'Active' : 'Archived';
                
                // Validate conversation ID is a valid GUID format for security
                const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
                if (!guidRegex.test(convoId)) {
                    console.warn('[MANAGE] Invalid conversation ID format:', convoId);
                    return '';
                }
                
                return `
                    <div data-modal-conversation-id="${convoId}" style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0;">
                        <div style="flex: 1; overflow: hidden;">
                            <div style="font-size: 14px; color: #333; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                                ${escapeHtml(convoTitle)}
                            </div>
                            <div style="font-size: 11px; color: #666; margin-top: 2px;">
                                <span style="color: ${statusColor};">● ${statusText}</span> · ${messageCount} message${messageCount !== 1 ? 's' : ''}
                            </div>
                        </div>
                        <button type="button" onclick="viewConversation('${convoId}', '${escapeHtml(convoTitle).replace(/'/g, "\\'")}')"
                            style="padding: 4px 12px; background: #9b59b6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; white-space: nowrap;">
                            View
                        </button>
                    </div>
                `;
            }).join('');
        }
        
        // Build reminders list HTML
        let remindersListHTML = '';
        if (groupReminders.length === 0) {
            remindersListHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No reminders yet</div>';
        } else {
            remindersListHTML = groupReminders.map(reminder => {
                const reminderId = getProp(reminder, 'id');
                const reminderName = getProp(reminder, 'name') || 'Untitled';
                const fileCount = getProp(reminder, 'fileCount') || 0;
                const conversationCount = getProp(reminder, 'conversationCount') || 0;
                
                return `
                    <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0;">
                        <div style="flex: 1; overflow: hidden;">
                            <div style="font-size: 14px; color: #333; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                                ${escapeHtml(reminderName)}
                            </div>
                            <div style="font-size: 11px; color: #666; margin-top: 2px;">
                                📄 ${fileCount} file${fileCount !== 1 ? 's' : ''} · 🗨️ ${conversationCount} conversation${conversationCount !== 1 ? 's' : ''}
                            </div>
                        </div>
                        <button type="button" onclick="manageGroupResources('${reminderId}', '${escapeHtml(reminderName).replace(/'/g, "\\'")}')"
                            style="padding: 4px 12px; background: #e67e22; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; white-space: nowrap;">
                            View
                        </button>
                    </div>
                `;
            }).join('');
        }
        
        // Attempt to fetch existing mapping for this group and include mapping UI
        let existingMapping = null;
        try {
            const mapRes = await fetchWithRetry(`/api/conversations/mapping?groupId=${groupId}`);
            if (mapRes.ok) {
                const mapJson = await mapRes.json().catch(() => null);
                existingMapping = mapJson?.conversationId || mapJson?.ConversationId || null;
            }
        } catch (mapErr) {
            console.warn('[MANAGE] Failed to fetch group->conversation mapping:', mapErr);
        }

        modalBody.innerHTML = `
            <div style="display: flex; flex-direction: column; gap: 12px;">
                <div style="display: flex; justify-content: space-between; align-items: center;">
                    <h3 style="margin: 0; font-size: 16px; color: #2c3e50;">Group Contents</h3>
                    <button type="button" onclick="closeManageGroupModal()" style="background: none; border: none; font-size: 20px; color: #95a5a6; cursor: pointer; padding: 0; line-height: 1;">×</button>
                </div>
                <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px;">
                    <div style="border: 2px solid #0b63a6; border-radius: 8px; padding: 12px; display: flex; flex-direction: column; position: relative;">
                        <div style="font-weight: 600; color: #2c3e50; margin-bottom: 8px; font-size: 14px;">Files</div>
                        <div id="modal-files-list" style="flex: 1; overflow-y: auto; border: 1px solid #e0e0e0; border-radius: 4px; background: white; max-height: 360px; min-height: 160px;">
                            ${filesListHTML}
                        </div>
                    </div>
                    <div style="border: 2px solid #9b59b6; border-radius: 8px; padding: 12px; display: flex; flex-direction: column; position: relative;">
                        <div style="font-weight: 600; color: #2c3e50; margin-bottom: 8px; font-size: 14px;">Conversations</div>
                        <div id="modal-conversations-list" style="flex: 1; overflow-y: auto; border: 1px solid #e0e0e0; border-radius: 4px; background: white; max-height: 360px; min-height: 160px;">
                            ${conversationsListHTML}
                        </div>
                    </div>
                    <div style="border: 2px solid #e67e22; border-radius: 8px; padding: 12px; display: flex; flex-direction: column; position: relative;">
                        <div style="font-weight: 600; color: #2c3e50; margin-bottom: 8px; font-size: 14px;">Reminders</div>
                        <div id="modal-reminders-list" style="flex: 1; overflow-y: auto; border: 1px solid #e0e0e0; border-radius: 4px; background: white; max-height: 360px; min-height: 160px;">
                            ${remindersListHTML}
                        </div>
                    </div>
                </div>
                <div style="border-top: 1px dashed #e0e0e0; padding-top: 12px; margin-top: 8px;">
                    <div style="font-weight: 600; color: #2c3e50; margin-bottom: 8px; font-size: 14px;">Group Conversation Mapping</div>
                        <div style="font-size: 13px; color: #666; margin-bottom: 8px;">This is a global server-side mapping from this group to an active conversation. Use it to force which conversation continues when queries target this group.</div>
                        <div id="mapping-prompt-area" style="display:flex; flex-direction:column; gap:8px;">
                            <div style="display:flex; gap:8px; align-items:center;">
                                <select id="mapping-conversation-select" style="flex:1; padding:8px; border:1px solid #ddd; border-radius:4px;"></select>
                                <button id="mapping-bind-btn" class="btn-gradient">Bind</button>
                                <button id="mapping-clear-btn" class="secondary">Clear</button>
                            </div>
                            <div style="display:flex; gap:8px; align-items:center;">
                                <input id="mapping-conversation-id" type="text" placeholder="Or paste ConversationId (GUID)" value="${existingMapping || ''}" style="flex:1; padding:8px; border:1px solid #ddd; border-radius:4px;" />
                                <button id="mapping-paste-save-btn" class="btn-gradient">Save Paste</button>
                            </div>
                            <div style="margin-top:6px; font-size:12px; color:#95a5a6;">Tip: pick a recent conversation or paste a ConversationId, then click Bind/Save. Clear removes the server-side mapping.</div>
                        </div>
                </div>
            </div>
        `;

        // Populate conversation picker
        try {
            const convSel = document.getElementById('mapping-conversation-select');
            if (convSel) {
                convSel.innerHTML = '<option value="">Loading...</option>';
                const convRes = await fetchWithRetry(`/api/conversations?groupId=${groupId}`);
                if (convRes.ok) {
                    const convJson = await convRes.json().catch(() => null);
                    const convs = convJson?.data || convJson?.Data || convJson || [];
                    if (convs && convs.length > 0) {
                        convSel.innerHTML = '';
                        convs.forEach(c => {
                            const id = getProp(c, 'id') || getProp(c, 'Id');
                            const title = getProp(c, 'title') || getProp(c, 'Title') || (getProp(c, 'messages') && getProp(c, 'messages')[0]?.content?.slice(0,50)) || id;
                            const updated = getProp(c, 'updatedAt') || getProp(c, 'UpdatedAt') || '';
                            const label = `${title}${updated ? ' — ' + new Date(updated).toLocaleString() : ''}`;
                            const opt = document.createElement('option');
                            opt.value = id;
                            opt.textContent = label;
                            convSel.appendChild(opt);
                        });
                        if (existingMapping) convSel.value = existingMapping;
                    } else {
                        convSel.innerHTML = '<option value="">No recent conversations</option>';
                    }
                } else {
                    convSel.innerHTML = '<option value="">Failed to load</option>';
                }
            }
        } catch (err) {
            console.warn('[MANAGE] Failed to load conversations for picker', err);
        }

        // Bind button
        const bindBtn = document.getElementById('mapping-bind-btn');
        bindBtn?.addEventListener('click', async () => {
            const convSel = document.getElementById('mapping-conversation-select');
            const selected = convSel?.value;
            if (!selected) { showMessage('Please choose a conversation to bind', 'error'); return; }
            try {
                const payload = { ResourceGroupId: groupId, ConversationId: selected };
                const res = await fetch('/api/conversations/mapping', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
                if (res.ok || res.status === 204) showMessage('Mapping bound successfully', 'success'); else showMessage('Failed to bind mapping', 'error');
            } catch (err) { console.error(err); showMessage('Failed to bind mapping', 'error'); }
        });

        // Paste-save button (manual GUID)
        const pasteSaveBtn = document.getElementById('mapping-paste-save-btn');
        pasteSaveBtn?.addEventListener('click', async () => {
            const input = document.getElementById('mapping-conversation-id');
            const val = input?.value.trim();
            if (!val) { showMessage('Please paste a ConversationId to save', 'error'); return; }
            try {
                const payload = { ResourceGroupId: groupId, ConversationId: val };
                const res = await fetch('/api/conversations/mapping', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
                if (res.ok || res.status === 204) showMessage('Mapping saved successfully', 'success'); else showMessage('Failed to save mapping', 'error');
            } catch (err) { console.error(err); showMessage('Failed to save mapping', 'error'); }
        });

        // Clear mapping button
        const clearBtn = document.getElementById('mapping-clear-btn');
        clearBtn?.addEventListener('click', async () => {
            try {
                const res = await fetch(`/api/conversations/mapping?groupId=${groupId}`, { method: 'DELETE' });
                if (res.ok || res.status === 204) {
                    showMessage('Mapping cleared', 'success');
                    const convSel = document.getElementById('mapping-conversation-select'); if (convSel) convSel.value = '';
                    const input = document.getElementById('mapping-conversation-id'); if (input) input.value = '';
                } else {
                    showMessage('Failed to clear mapping', 'error');
                }
            } catch (err) { console.error(err); showMessage('Failed to clear mapping', 'error'); }
        });
        
        document.getElementById('manage-group-modal').classList.add('active');
    } catch (error) {
        showMessage('Error loading group resources: ' + error.message, 'error');
    }
}

// Quick add to group functionality
async function loadAllGroups(force = false) {
    try {
        console.log('[LOAD ALL GROUPS] Force refresh:', force);
        const response = await fetchWithRetry(`${API_BASE}/groups?page=1&pageSize=1000`);
        
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        
        const result = await response.json();
        // Handle both lowercase and uppercase from API
        state.allGroups = result.data || result.Data || [];
        console.log('[LOAD ALL GROUPS] Loaded', state.allGroups.length, 'groups');
    } catch (error) {
        console.error('Error loading all groups:', error);
    }
}

async function showQuickAddToGroup(resourceId, resourceType, resourceName) {
    // Always refresh groups to ensure we have the latest
    console.log('[QUICK ADD] Refreshing groups list...');
    await loadAllGroups(true);
    
    if (state.allGroups.length === 0) {
        showMessage('No groups available. Create a group first.', 'error');
        return;
    }
    
    const modalBody = document.getElementById('modal-content-body');
    modalBody.innerHTML = `
        <h4 style="margin-bottom: 15px;">Add ${resourceType === 'file' ? 'File' : 'Prompt'} to Group</h4>
        <p style="margin-bottom: 15px; color: #555;">${escapeHtml(resourceName)}</p>
        
        <div class="section">
            <h5 style="margin-bottom: 10px;">Select Groups:</h5>
            <div class="checkbox-group">
                ${state.allGroups.map(group => `
                    <div class="checkbox-item">
                        <label>
                            <input type="checkbox" 
                                value="${group.id}" 
                                data-action="quick-toggle-${resourceType}"
                                data-group-id="${group.id}"
                                data-resource-id="${resourceId}">
                            ${escapeHtml(group.name)}
                        </label>
                    </div>
                `).join('')}
            </div>
        </div>
        <div style="margin-top: 20px; text-align: right;">
            <button id="quick-add-submit" class="secondary">Done</button>
        </div>
    `;
    
    // Attach event listeners for checkboxes
    modalBody.querySelectorAll(`[data-action="quick-toggle-${resourceType}"]`).forEach(checkbox => {
        checkbox.addEventListener('change', async (e) => {
            if (resourceType === 'file') {
                await toggleFileInGroup(e.target.dataset.groupId, e.target.dataset.resourceId, e.target.checked);
            } else {
                await togglePromptInGroup(e.target.dataset.groupId, e.target.dataset.resourceId, e.target.checked);
            }
        });
    });
    
    modalBody.querySelector('#quick-add-submit').addEventListener('click', () => {
        closeManageGroupModal();
        // Refresh only the relevant data based on resource type
        if (resourceType === 'file') {
            loadFiles(true);
        } else {
            loadPrompts(true);
        }
        loadGroups(true); // Always refresh groups to show updated counts
    });
    
    document.getElementById('manage-group-modal').classList.add('active');
}

async function toggleFileInGroup(groupId, fileId, add) {
    try {
        const url = `${API_BASE}/groups/${groupId}/files/${fileId}`;
        const method = add ? 'POST' : 'DELETE';
        
        const response = await fetch(url, { method });
        
        if (response.ok || response.status === 204) {
            showMessage(add ? 'File added to group' : 'File removed from group');
            
            // Update the group in state cache
            const groupInState = state.groups.data.find(g => g.id === groupId);
            if (groupInState) {
                if (!groupInState.files) groupInState.files = [];
                if (add) {
                    // Add file to cache if not already there
                    if (!groupInState.files.find(f => f.id === fileId)) {
                        const allFiles = state.files.data;
                        const fileToAdd = allFiles.find(f => f.id === fileId);
                        if (fileToAdd) groupInState.files.push(fileToAdd);
                    }
                } else {
                    // Remove file from cache
                    groupInState.files = groupInState.files.filter(f => f.id !== fileId);
                }
            }
            
            // Refresh groups display to show updated counts
            loadGroups(true);
            await loadAllGroups();
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error updating group', 'error');
            throw new Error(error.detail || 'Error updating group');
        }
    } catch (error) {
        showMessage('Error updating group: ' + error.message, 'error');
        throw error;
    }
}

async function togglePromptInGroup(groupId, promptId, add) {
    try {
        const url = `${API_BASE}/groups/${groupId}/prompts/${promptId}`;
        const method = add ? 'POST' : 'DELETE';
        
        const response = await fetch(url, { method });
        
        if (response.ok || response.status === 204) {
            showMessage(add ? 'Prompt added to group' : 'Prompt removed from group');
            
            // Update the group in state cache
            const groupInState = state.groups.data.find(g => g.id === groupId);
            if (groupInState) {
                if (!groupInState.prompts) groupInState.prompts = [];
                if (add) {
                    // Add prompt to cache if not already there
                    if (!groupInState.prompts.find(p => p.id === promptId)) {
                        const allPrompts = state.prompts.data;
                        const promptToAdd = allPrompts.find(p => p.id === promptId);
                        if (promptToAdd) groupInState.prompts.push(promptToAdd);
                    }
                } else {
                    // Remove prompt from cache
                    groupInState.prompts = groupInState.prompts.filter(p => p.id !== promptId);
                }
            }
            
            // Refresh groups display to show updated counts
            loadGroups(true);
            await loadAllGroups();
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error updating group', 'error');
            throw new Error(error.detail || 'Error updating group');
        }
    } catch (error) {
        showMessage('Error updating group: ' + error.message, 'error');
        throw error;
    }
}

function closeManageGroupModal() {
    document.getElementById('manage-group-modal').classList.remove('active');
}

// Preview file from reminder modal
async function previewReminderFile(fileId, fileName) {
    const modal = document.getElementById('reminder-file-preview-modal');
    const modalContent = document.getElementById('reminder-file-preview-content');
    const modalTitle = document.getElementById('reminder-file-preview-title');
    
    modalTitle.textContent = fileName;
    modalContent.innerHTML = '<p style="color: #95a5a6; text-align: center; padding: 20px;">Loading preview...</p>';
    modal.style.display = 'flex';
    
    const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
    const previewUrl = `${API_BASE}/files/${fileId}/preview?v=${cacheBuster}`;
    const ext = fileName.split('.').pop().toLowerCase();
    
    if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg'].includes(ext)) {
        const img = new Image();
        img.onload = function() {
            modalContent.innerHTML = `<img src="${previewUrl}" alt="${escapeHtml(fileName)}" style="max-width: 100%; max-height: 55vh; width: auto; height: auto; border-radius: 4px; object-fit: contain; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">`;
        };
        img.onerror = function() {
            modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load image. The file may not exist or there was an error.</p>';
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
                modalContent.innerHTML = `<embed src="${blobUrl}" type="application/pdf" width="100%" height="55vh" style="border-radius: 4px; min-height: 400px;">`;
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
                <p style="color: #95a5a6; font-size: 12px; margin-top: 10px;">File: ${escapeHtml(fileName)}</p>
            </div>
        `;
    }
}

async function resolveDocumentIdByName(fileName) {
    if (!fileName) return null;
    try {
        const response = await fetch('/api/documents?page=1&pageSize=1000');
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
                modalContent.innerHTML = `<embed src="${blobUrl}" type="application/pdf" width="100%" height="55vh" style="border-radius: 4px; min-height: 400px;">`;
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

function closeReminderFilePreviewModal() {
    try {
        const modalContent = document.getElementById('reminder-file-preview-content');
        const blobUrl = modalContent?.dataset?.blobUrl;
        if (blobUrl) {
            URL.revokeObjectURL(blobUrl);
            delete modalContent.dataset.blobUrl;
        }
    } catch (e) {
        console.warn('Failed to revoke preview blob URL', e);
    }
    document.getElementById('reminder-file-preview-modal').style.display = 'none';
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
document.getElementById('close-modal-btn').addEventListener('click', closeManageGroupModal);

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

// Wait for API to be ready
async function waitForAPI(maxRetries = 10, delayMs = 500) {
    for (let i = 0; i < maxRetries; i++) {
        try {
            console.log(`[API Health] Checking API health (attempt ${i + 1}/${maxRetries})...`);
            const response = await fetch(`${API_BASE}/files?page=1&pageSize=1`);
            if (response.ok) {
                console.log('[API Health] API is ready!');
                return true;
            }
            console.warn(`[API Health] API returned ${response.status}, retrying...`);
        } catch (error) {
            console.warn(`[API Health] API not ready yet:`, error.message);
        }
        
        if (i < maxRetries - 1) {
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
        showMessage('API is not responding. Please refresh the page.', 'error');
        alert('ERROR: The backend API is not responding. Please wait a moment and refresh the page.');
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

        // Start hub initialization in background so UI handlers attach immediately
        initConversationHub();
        initResourceHub();
        initTaskHub();
        
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
        };
        
        // Reset attach-file section to closed state on every page load
        if (promptFileSection) {
            promptFileSection.style.setProperty('display', 'none', 'important');
            promptFileSection.style.setProperty('visibility', 'hidden', 'important');
        }
        const _initAttachBtn = document.getElementById('prompt-show-file-btn');
        if (_initAttachBtn) _initAttachBtn.textContent = 'Attach file';

        updatePromptFilesList();
        
        // Show file section when clicking "add file+" button
        document.getElementById('prompt-show-file-btn')?.addEventListener('click', () => {
            if (ADMIN_DEBUG) console.log('prompt-show-file-btn clicked');
            // Prefer a global shim if present (ensures reliability across browsers/load timing)
            if (typeof window.togglePromptFileSection === 'function') {
                if (ADMIN_DEBUG) console.log('calling window.togglePromptFileSection()');
                try { window.togglePromptFileSection(); return; } catch (e) { /* fallback below */ }
            }
            // Fallback: toggle using computed style for robustness
            const computed = window.getComputedStyle(promptFileSection);
            const isCurrentlyHidden = computed ? computed.display === 'none' : (promptFileSection.style.display === 'none' || !promptFileSection.style.display);
            
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
            if (ADMIN_DEBUG) console.log('promptFilesIcon clicked (paperclip)');
            // Prefer global shim when available
            if (typeof window.showPromptFilesPanel === 'function') {
                if (ADMIN_DEBUG) console.log('calling window.showPromptFilesPanel()');
                try { window.showPromptFilesPanel(); return; } catch (e) { /* fallback below */ }
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
                    promptPreviewFile = file;
                    if (promptPreviewFilename) {
                        promptPreviewFilename.textContent = file.name;
                    }
                    
                    // Show actual preview based on file type (like reminders)
                    const ext = file.name.split('.').pop().toLowerCase();
                    if (['jpg', 'jpeg', 'png', 'gif'].includes(ext)) {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            promptPreviewContent.innerHTML = `<img src="${e.target.result}" style="max-width: 100%; max-height: 200px; border-radius: 4px; object-fit: contain;">`;
                        };
                        reader.readAsDataURL(file);
                    } else if (ext === 'pdf') {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            promptPreviewContent.innerHTML = `<embed src="${e.target.result}" type="application/pdf" width="100%" height="200px" style="border-radius: 4px;">`;
                        };
                        reader.readAsDataURL(file);
                    } else if (['wav', 'mp3', 'm4a', 'ogg', 'webm'].includes(ext)) {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            promptPreviewContent.innerHTML = `
                                <div style="text-align: center;">
                                    <div style="font-size: 64px; margin-bottom: 10px;">🎤</div>
                                    <audio controls style="width: 100%; margin-top: 10px;">
                                        <source src="${e.target.result}" type="audio/${ext}">
                                    </audio>
                                </div>
                            `;
                        };
                        reader.readAsDataURL(file);
                    } else if (ext === 'md') {
                        // Render markdown files
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            const text = e.target.result;
                            const renderedHtml = renderMarkdown(text);
                            promptPreviewContent.innerHTML = `<div style="text-align: left; max-height: 200px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px;">${renderedHtml}</div>`;
                        };
                        reader.readAsText(file);
                    } else if (['txt', 'json', 'xml', 'csv'].includes(ext)) {
                        // Show text files as plain text
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            const text = e.target.result;
                            promptPreviewContent.innerHTML = `<pre style="text-align: left; max-height: 200px; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 11px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
                        };
                        reader.readAsText(file);
                    } else {
                        promptPreviewContent.innerHTML = `
                            <div style="text-align: center;">
                                <div style="font-size: 64px; margin-bottom: 10px;">📄</div>
                                <div style="color: #2f3a3f; font-size: 12px;">Preview not available</div>
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
                    
                    try {
                        // Show progress bar
                        if (promptUploadProgress) {
                            promptUploadProgress.style.display = 'block';
                        }
                        const progressBar = document.getElementById('prompt-upload-progress-bar');
                        if (progressBar) {
                            progressBar.style.width = '0%';
                        }
                        
                        // Check if this is an audio file - handle via transcription API
                        if (['wav', 'mp3', 'm4a', 'ogg', 'webm'].includes(ext)) {
                            // Read file as base64 for transcription API
                            const reader = new FileReader();
                            reader.onload = async function(e) {
                                try {
                                    const base64Audio = e.target.result.split(',')[1]; // Remove data:audio/...;base64, prefix
                                    
                                    // Show transcribing status
                                    if (promptPreviewContent) {
                                        promptPreviewContent.innerHTML = `
                                            <div style="text-align: center;">
                                                <div style="font-size: 48px; margin-bottom: 10px;">🎤</div>
                                                <div style="color: #2c3e50; font-weight: 500;">Transcribing audio...</div>
                                                <div style="margin-top: 10px;">
                                                    <div style="width: 100%; height: 6px; background: #e0e0e0; border-radius: 3px; overflow: hidden;">
                                                        <div id="audio-transcribe-progress" style="width: 0%; height: 100%; background: linear-gradient(90deg, #0b63a6, #6bb9f0, #0b63a6); animation: promptUploadMove 1.2s linear infinite;"></div>
                                                    </div>
                                                </div>
                                            </div>
                                        `;
                                    }
                                    
                                    // Call transcription API
                                    const response = await fetch('/api/transcribe', {
                                        method: 'POST',
                                        headers: {
                                            'Content-Type': 'application/json'
                                        },
                                        body: JSON.stringify({
                                            audioBase64: base64Audio
                                        })
                                    });
                                    
                                    if (!response.ok) {
                                        throw new Error('Transcription failed');
                                    }
                                    
                                    const result = await response.json();
                                    const transcription = result.transcription || '';
                                    
                                    // Hide progress bar
                                    if (promptUploadProgress) {
                                        promptUploadProgress.style.display = 'none';
                                    }
                                    
                                    // Show transcription result
                                    if (promptPreviewContent) {
                                        promptPreviewContent.innerHTML = `
                                            <div style="text-align: left; padding: 10px;">
                                                <div style="font-size: 14px; color: #2c3e50; margin-bottom: 10px; font-weight: 600;">
                                                    ✓ Transcription Complete
                                                </div>
                                                <div style="background: #f8f9fa; padding: 10px; border-radius: 4px; max-height: 150px; overflow-y: auto; font-size: 13px; line-height: 1.5;">
                                                    ${transcription || 'No text detected'}
                                                </div>
                                            </div>
                                        `;
                                    }
                                    
                                    // Add transcription to prompt input
                                    const promptInput = document.getElementById('prompt-name');
                                    if (promptInput && transcription) {
                                        const currentText = promptInput.value.trim();
                                        promptInput.value = currentText ? `${currentText}\n\n[Audio]: ${transcription}` : transcription;
                                    }
                                    
                                    showMessage('Audio transcribed successfully!', 'success');
                                    
                                    // Clear file input
                                    promptFileInput.value = '';
                                    
                                } catch (error) {
                                    console.error('Transcription error:', error);
                                    showMessage('Audio transcription failed: ' + error.message, 'error');
                                    if (promptUploadProgress) {
                                        promptUploadProgress.style.display = 'none';
                                    }
                                }
                            };
                            reader.readAsDataURL(file);
                            return; // Skip regular upload for audio files
                        }
                        
                        // Regular file upload (documents, images, etc.)
                        const formData = new FormData();
                        formData.append('file', file);
                        
                        const xhr = new XMLHttpRequest();
                        
                        // Track upload progress
                        xhr.upload.addEventListener('progress', (e) => {
                            if (e.lengthComputable && progressBar) {
                                const percentComplete = (e.loaded / e.total) * 100;
                                progressBar.style.width = percentComplete + '%';
                            }
                        });
                        
                        // Handle upload completion
                        const uploadPromise = new Promise((resolve, reject) => {
                            xhr.addEventListener('load', () => {
                                if (xhr.status >= 200 && xhr.status < 300) {
                                    try {
                                        const fileData = JSON.parse(xhr.responseText);
                                        resolve(fileData);
                                    } catch (e) {
                                        reject(new Error('Invalid JSON response'));
                                    }
                                } else {
                                    reject(new Error(`Upload failed: ${xhr.responseText}`));
                                }
                            });
                            xhr.addEventListener('error', () => reject(new Error('Network error')));
                            xhr.addEventListener('abort', () => reject(new Error('Upload cancelled')));
                        });
                        
                        // Upload to Documents API so RAG can use the attachment
                        let uploadGroupId = activePromptGroupId || null;
                        if (!uploadGroupId && activeConversationId) {
                            try {
                                const conv = await (await fetch(`/api/conversations/${activeConversationId}`)).json().catch(() => null);
                                uploadGroupId = conv?.resourceGroupId || conv?.ResourceGroupId || null;
                            } catch (err) {
                                console.warn('[UPLOAD] Failed to resolve conversation group for file:', err);
                            }
                        }
                        // Pass groupIds as a query parameter (controller uses [FromQuery], not [FromForm])
                        const uploadUrl = uploadGroupId ? `/api/documents/upload?groupIds=${encodeURIComponent(uploadGroupId)}` : '/api/documents/upload';
                        xhr.open('POST', uploadUrl);
                        xhr.send(formData);
                        
                        const fileData = await uploadPromise;
                        const documentId = fileData.id || fileData.Id;
                        
                        // Add uploaded file to list with server ID
                        promptSelectedFiles.push({
                            file: file, 
                            name: file.name, 
                            selected: false,
                            documentId: documentId
                        });
                        window.promptSelectedFiles = promptSelectedFiles;
                        // Refresh name once server slug is assigned asynchronously
                        const newEntry = promptSelectedFiles[promptSelectedFiles.length - 1];
                        scheduleFileNameRefresh(newEntry, documentId, file.name);
                        updatePromptFilesList();
                        showMessage(`File "${file.name}" uploaded successfully`, 'success');
                        
                        // Clear preview
                        promptPreviewFile = null;
                        if (promptPreviewFilename) promptPreviewFilename.textContent = '';
                        if (promptPreviewContent) promptPreviewContent.innerHTML = `<div style="font-size: 48px; color: #95a5a6;">📄</div>`;
                        if (promptFileInput) promptFileInput.value = '';
                        
                        // Reload files list to show the new file (documents are listed via /api/documents)
                        if (typeof loadFiles === 'function') {
                            loadFiles(true);
                        }
                    } catch (error) {
                        console.error('Error uploading file:', error);
                        showMessage(`Failed to upload file: ${error.message}`, 'error');
                        if (promptFileInput) promptFileInput.value = '';
                    } finally {
                        // Hide progress bar after a short delay
                        setTimeout(() => {
                            if (promptUploadProgress) {
                                promptUploadProgress.style.display = 'none';
                            }
                        }, 1000);
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
                            const typeIcon = documentType === 'pdf' ? '📄' : 
                                           documentType === 'markdown' ? '📝' :
                                           documentType === 'image' ? '🖼️' : 
                                           documentType === 'docx' ? '📘' :
                                           documentType === 'xml' ? '🔖' : '📋';
                            
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

        const sendPrompt = async () => {
            const promptText = document.getElementById('prompt-name')?.value;
            const responseContent = document.getElementById('response-content');
            const sendBtn = document.getElementById('prompt-send-btn');
            const cancelBtn = document.getElementById('prompt-cancel-btn');
            let lastQueryResult = null;
            
            if (!promptText || !promptText.trim()) {
                showMessage('Please enter a prompt text', 'error');
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
                    let id = f.documentId || f.id;
                    if (!id && f.name) {
                        id = await resolveDocumentIdByName(f.name);
                        if (id) f.documentId = id;
                    }
                    if (id) documentIds.push(id);
                }
                const includeAllGroups = document.getElementById('prompt-include-all')?.checked;
                const payload = { Query: promptText, TopK: 5 };
                if (documentIds.length > 0) {
                    payload.DocumentIds = documentIds;
                }
                if (activePromptGroupId && !includeAllGroups) {
                    payload.GroupIds = [activePromptGroupId];
                }
                if (activeConversationId) {
                    payload.ConversationId = activeConversationId;
                }
                // Ensure any locally attached files are uploaded to the server first
                if (promptSelectedFiles && promptSelectedFiles.length > 0) {
                    for (let i = 0; i < promptSelectedFiles.length; i++) {
                        const f = promptSelectedFiles[i];
                        if (!f.documentId && f.file) {
                            try {
                                const fd = new FormData();
                                fd.append('file', f.file);

                                // Upload to Documents API so RAG can index and use the attachment
                                let uploadGroupId = activePromptGroupId || null;
                                if (!uploadGroupId && activeConversationId) {
                                    const conv = await (await fetch(`/api/conversations/${activeConversationId}`)).json().catch(() => null);
                                    uploadGroupId = conv?.resourceGroupId || conv?.ResourceGroupId || null;
                                }
                                // Pass groupIds as a query parameter (controller uses [FromQuery], not [FromForm])
                                const uploadUrlFallback = uploadGroupId ? `/api/documents/upload?groupIds=${encodeURIComponent(uploadGroupId)}` : '/api/documents/upload';
                                const upRes = await fetch(uploadUrlFallback, { method: 'POST', body: fd });
                                if (upRes.ok) {
                                    const fdJson = await upRes.json().catch(() => null);
                                    const newId = fdJson?.id || fdJson?.Id;
                                    if (newId) {
                                        f.documentId = newId;
                                        scheduleFileNameRefresh(f, newId, f.name);
                                        console.log('[SEND PROMPT] Uploaded attached file', f.name, '=>', newId);
                                    }
                                } else {
                                    console.warn('[SEND PROMPT] Failed to upload attached file:', f.name);
                                }
                            } catch (upErr) {
                                console.warn('[SEND PROMPT] Error uploading attached file:', upErr);
                            }
                        }
                    }
                    // sync window global
                    window.promptSelectedFiles = promptSelectedFiles;
                    updatePromptFilesList();
                }

                // Recompute selected files and documentIds after any uploads
                const selectedFilesAfterUpload = promptSelectedFiles.some(f => f.selected)
                    ? promptSelectedFiles.filter(f => f.selected)
                    : promptSelectedFiles;
                const documentIdsAfterUpload = selectedFilesAfterUpload
                    .map(f => f.documentId || f.id)
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
                let result;
                try {
                    // Try to parse JSON body (may throw)
                    result = await queryResponse.json();
                    lastQueryResult = result;
                    console.log('[SEND PROMPT] documents query response JSON:', result);
                } catch (parseErr) {
                    const text = await queryResponse.text().catch(() => '');
                    console.error('[SEND PROMPT] Failed to parse JSON response. Raw response:', text);
                    throw new Error('Invalid JSON response from server');
                }

                if (!queryResponse.ok) {
                    const errorMsg = result?.error || result?.details || result?.message || `Server error: ${queryResponse.status}`;
                    throw new Error(errorMsg);
                }

                const responseConversationId = result.conversationId || result.ConversationId;
                if (responseConversationId) {
                    activeConversationId = responseConversationId;
                    await joinConversationHubGroup(responseConversationId);
                    // Server will persist group->conversation mapping. If we don't have an active group,
                    // ask the server for the conversation to determine the associated group and activate it.
                    if (!activePromptGroupId) {
                        await syncActiveGroupFromConversation(responseConversationId);
                    }

                    // Attach documents to the conversation's group for persistence and RAG scoping.
                    // Uses POST /api/documents/{id}/move which operates on DocumentGroups (DocumentMetadata
                    // system). The old /api/admin/groups/{groupId}/files/{fileId} endpoint operates on
                    // ManagedFiles (different table/IDs) and would always return 404 here.
                    if (documentIdsAfterUpload && documentIdsAfterUpload.length > 0) {
                        try {
                            const convRes = await fetch(`/api/conversations/${responseConversationId}`);
                            const convJson = convRes.ok ? await convRes.json().catch(() => null) : null;
                            const convGroupId = convJson?.resourceGroupId || convJson?.ResourceGroupId || null;
                            if (convGroupId) {
                                for (const fileId of documentIdsAfterUpload) {
                                    try {
                                        await fetch(`/api/documents/${fileId}/move`, {
                                            method: 'POST',
                                            headers: { 'Content-Type': 'application/json' },
                                            body: JSON.stringify({ groupIds: [convGroupId], regenerateEmbeddings: false })
                                        });
                                    } catch (attachErr) {
                                        console.warn('[SEND PROMPT] Failed to attach document to group:', attachErr);
                                    }
                                }
                            }
                        } catch (err) {
                            console.warn('[SEND PROMPT] Failed to attach documents to conversation group:', err);
                        }
                    }

                    // Ensure the conversation stays linked to the active group (for group history)
                    if (activePromptGroupId) {
                        try {
                            const convRes = await fetch(`/api/conversations/${responseConversationId}`);
                            const convJson = convRes.ok ? await convRes.json().catch(() => null) : null;
                            const convGroupId = convJson?.resourceGroupId || convJson?.ResourceGroupId || null;
                            if (!convGroupId || String(convGroupId) !== String(activePromptGroupId)) {
                                await fetch(`/api/conversations/${responseConversationId}/group`, {
                                    method: 'PUT',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ resourceGroupId: activePromptGroupId })
                                });
                            }
                        } catch (err) {
                            console.warn('[SEND PROMPT] Failed to link conversation to active group:', err);
                        }
                    }
                }

                const answer = result.answer || result.Answer || result.AnswerText || result?.AnswerHtml || result?.answerText || 'No response received';
                const sources = result.referencedSources || result.ReferencedSources || result.sources || result.Sources || [];

                // Get current attached files
                const attachedFiles = promptSelectedFiles.map(f => ({
                    name: f.file?.name || f.name,
                    id: f.documentId || f.id
                }));
                
                // Add to conversation history with files and sources
                currentConversationHistory.push({role: 'user', content: promptText, files: attachedFiles});
                currentConversationHistory.push({role: 'bot', content: answer, sources});

                
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

                    return `
                        <div class="conversation-message" style="margin-bottom: 12px;">
                            <div class="conversation-message-role ${msg.role === 'user' ? 'conversation-message-user' : 'conversation-message-bot'}" style="font-weight: 600; margin-bottom: 4px; display: flex; align-items: center; gap: 8px;">
                                <span>${msg.role === 'user' ? 'User' : 'Bot'}:</span>
                                ${msg.files && msg.files.length > 0 ? `<button type="button" onclick="showAttachedFilesModal('${filesData}')" style="background: none; border: none; font-size: 16px; color: #0b63a6; cursor: pointer; padding: 0;" title="${escapeHtml(filesTitle)}">📎</button>` : ''}
                            </div>
                            <div class="conversation-message-content" style="padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
                            ${msgSourcesHtml}
                        </div>
                    `;
                }).join('');
                responseContent.innerHTML = `
                    <div class="conversation-history-container" style="max-height: 500px; overflow-y: auto; padding: 15px; border-radius: 8px;">
                        ${conversationHTML}
                    </div>
                `;
                
                // Clear input for next message
                document.getElementById('prompt-name').value = '';
                
                showMessage('Response received successfully', 'success');

                apiCache.invalidate('/api/conversations');
                await loadPrompts(true);
                await loadGroups(true);
                await loadAllGroups(true);

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
        document.getElementById('prompt-name')?.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                sendPrompt();
            }
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
                activePromptGroupId = null;
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
        let groupSelectedFiles = [];
        window.groupSelectedFiles = groupSelectedFiles; // Expose to global scope
        let groupPreviewFile = null;
        const groupFileInput = document.getElementById('group-file-input');
        const groupFilesList = document.getElementById('group-files-list');
        const groupFilesIcon = document.getElementById('group-files-icon');
        const groupFileSection = document.getElementById('group-file-section');
        const groupFilesDropdown = document.getElementById('group-files-dropdown');
        const groupFilesListDropdown = document.getElementById('group-files-list-dropdown');
        const groupPreviewContent = document.getElementById('group-preview-content');
        const groupPreviewFilename = document.getElementById('group-preview-filename');
        
        // Initialize updateGroupFilesList function first
        function updateGroupFilesList() {
            // Update paperclip icon color
            if (groupFilesIcon) {
                if (groupSelectedFiles.length === 0) {
                    groupFilesIcon.style.color = '#95a5a6'; // Gray when no files
                } else {
                    groupFilesIcon.style.color = '#0b63a6'; // Blue when files present
                }
            }
            
            // Update files list in Files section with checkboxes (only show if files exist)
            if (groupFilesList) {
                if (groupSelectedFiles.length === 0) {
                    groupFilesList.innerHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No attachments yet</div>';
                } else {
                    groupFilesList.innerHTML = groupSelectedFiles.map((fileObj, index) => `
                        <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0; ${fileObj.selected ? 'background: #f0f0f0;' : ''}">
                            <input type="checkbox" ${fileObj.selected ? 'checked' : ''} style="width: 16px; height: 16px; cursor: pointer; margin: 0;" onclick="toggleGroupFileSelection(${index})">
                            <span onclick="toggleGroupFileSelection(${index})" style="font-size: 14px; color: #333; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; user-select: none;">
                                ${escapeHtml(fileObj.name)}
                            </span>
                            <button type="button" onclick="event.stopPropagation(); previewGroupFile(${index})" style="padding: 4px 12px; background: #0b63a6; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; white-space: nowrap;">Preview</button>
                        </div>
                    `).join('');
                }
            }
            
            // Sync window.groupSelectedFiles
            window.groupSelectedFiles = groupSelectedFiles;
            
            // Update dropdown list
            if (groupFilesListDropdown) {
                if (groupSelectedFiles.length === 0) {
                    groupFilesListDropdown.innerHTML = '<div style="color: #95a5a6; text-align: center; padding: 10px;">No attachments</div>';
                } else {
                    groupFilesListDropdown.innerHTML = groupSelectedFiles.map((fileObj, index) => `
                        <div style="display: flex; justify-content: space-between; align-items: center; padding: 8px; border: 1px solid #ddd; border-radius: 4px;">
                            <span style="font-size: 12px; color: #2c3e50;">${escapeHtml(fileObj.name)}</span>
                            <button type="button" onclick="removeGroupFile(${index})" style="padding: 2px 8px; background: #e74c3c; color: white; border: none; border-radius: 3px; cursor: pointer; font-size: 11px;">Delete</button>
                        </div>
                    `).join('');
                }
            }
        }
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
                            groupPreviewContent.innerHTML = `<embed src="${e.target.result}" type="application/pdf" width="100%" height="200px" style="border-radius: 4px;">`;
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
                        window.groupSelectedFiles = groupSelectedFiles;
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
                    window.groupSelectedFiles = groupSelectedFiles;
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
            window.groupSelectedFiles = groupSelectedFiles;
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
        window.toggleGroupFileSelection = function(index) {
            groupSelectedFiles[index].selected = !groupSelectedFiles[index].selected;
            window.groupSelectedFiles = groupSelectedFiles;
            updateGroupFilesList();
        };
        
        // Preview group file
        window.previewGroupFile = function(index) {
            const fileObj = groupSelectedFiles[index];
            if (!fileObj || !fileObj.file) {
                showMessage('File not available for preview', 'error');
                return;
            }
            
            const file = fileObj.file;
            const modal = document.getElementById('group-file-preview-modal');
            const modalContent = document.getElementById('group-file-preview-content');
            const modalTitle = document.getElementById('group-file-preview-title');
            
            modalTitle.textContent = file.name;
            modalContent.innerHTML = '<p style="color: #95a5a6; text-align: center; padding: 20px;">Loading preview...</p>';
            modal.style.display = 'flex';
            
            const ext = file.name.split('.').pop().toLowerCase();
            
            if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg'].includes(ext)) {
                const reader = new FileReader();
                reader.onload = function(e) {
                    modalContent.innerHTML = `<img src="${e.target.result}" style="max-width: 100%; max-height: 55vh; width: auto; height: auto; border-radius: 4px; object-fit: contain;">`;
                };
                reader.onerror = function() {
                    modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load image.</p>';
                };
                reader.readAsDataURL(file);
            } else if (ext === 'pdf') {
                const reader = new FileReader();
                reader.onload = function(e) {
                    modalContent.innerHTML = `<embed src="${e.target.result}" type="application/pdf" width="100%" height="55vh" style="border-radius: 4px; min-height: 400px;">`;
                };
                reader.onerror = function() {
                    modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load PDF.</p>';
                };
                reader.readAsDataURL(file);
            } else if (['txt', 'json', 'xml', 'csv', 'md'].includes(ext)) {
                const reader = new FileReader();
                reader.onload = function(e) {
                    const text = e.target.result;
                    modalContent.innerHTML = `<pre style="text-align: left; max-height: 55vh; overflow: auto; padding: 15px; background: #f8f9fa; border-radius: 4px; font-size: 12px; white-space: pre-wrap; word-wrap: break-word;">${escapeHtml(text)}</pre>`;
                };
                reader.onerror = function() {
                    modalContent.innerHTML = '<p style="color: #e74c3c; text-align: center; padding: 20px;">Failed to load text file.</p>';
                };
                reader.readAsText(file);
            } else {
                modalContent.innerHTML = `
                    <div style="text-align: center; padding: 40px;">
                        <div style="font-size: 64px; margin-bottom: 20px;">📄</div>
                        <p style="color: #2f3a3f; font-size: 14px;">Preview not available for this file type.</p>
                        <p style="color: #95a5a6; font-size: 12px; margin-top: 10px;">File: ${escapeHtml(file.name)}</p>
                        <p style="color: #95a5a6; font-size: 12px;">Size: ${formatFileSize(file.size)}</p>
                    </div>
                `;
            }
        };
        
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
        window.removeGroupFile = function(index) {
            groupSelectedFiles.splice(index, 1);
            window.groupSelectedFiles = groupSelectedFiles;
            updateGroupFilesList();
        };
        
        // Delete selected files from Files panel
        window.deleteSelectedGroupFiles = function() {
            const selectedCount = groupSelectedFiles.filter(f => f.selected).length;
            if (selectedCount === 0) {
                return; // Nothing to delete
            }
            groupSelectedFiles = groupSelectedFiles.filter(fileObj => !fileObj.selected);
            updateGroupFilesList();
        };
        
        // Setup reminder file management (separate from groups)
        // reminderSelectedFiles is now declared at top level (line 673)
        let reminderPreviewFile = null;
        let reminderPreviewToken = 0;
        const reminderFileInput = document.getElementById('reminder-file-input');
        const reminderFilesList = document.getElementById('reminder-files-list');
        const reminderFilesIcon = document.getElementById('reminder-files-icon');
        const reminderFileSection = document.getElementById('reminder-file-section');
        const reminderPreviewContent = document.getElementById('reminder-preview-content');
        const reminderPreviewFilename = document.getElementById('reminder-preview-filename');
        
        // Initialize updateReminderFilesList function
        function updateReminderFilesList() {
            if (reminderFilesIcon) {
                if (reminderSelectedFiles.length === 0) {
                    reminderFilesIcon.style.color = '#95a5a6';
                } else {
                    reminderFilesIcon.style.color = '#0b63a6';
                }
            }
            
            if (reminderFilesList) {
                if (reminderSelectedFiles.length === 0) {
                    reminderFilesList.innerHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No attachments yet</div>';
                } else {
                    reminderFilesList.innerHTML = reminderSelectedFiles.map((fileObj, index) => `
                        <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0; ${fileObj.selected ? 'background: #f0f0f0;' : ''}">
                            <input type="checkbox" ${fileObj.selected ? 'checked' : ''} style="width: 16px; height: 16px; cursor: pointer; margin: 0;" onclick="toggleReminderFileSelection(${index})">
                            <span onclick="toggleReminderFileSelection(${index})" style="font-size: 14px; color: #333; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; user-select: none;">
                                ${escapeHtml(fileObj.name)}
                            </span>
                        </div>
                    `).join('');
                }
            }

            window.reminderSelectedFiles = reminderSelectedFiles;
        }
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
                            reminderPreviewContent.innerHTML = `<embed src="${e.target.result}" type="application/pdf" width="100%" height="200px" style="border-radius: 4px;">`;
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
        window.toggleReminderFileSelection = function(index) {
            if (reminderSelectedFiles[index]) {
                reminderSelectedFiles[index].selected = !reminderSelectedFiles[index].selected;
                updateReminderFilesList();
            }
        };
        
        // Delete selected files
        window.deleteSelectedReminderFiles = function() {
            const selectedCount = reminderSelectedFiles.filter(f => f.selected).length;
            if (selectedCount === 0) {
                return;
            }
            reminderSelectedFiles = reminderSelectedFiles.filter(fileObj => !fileObj.selected);
            updateReminderFilesList();

            if (reminderSelectedFiles.length === 0) {
                reminderPreviewFile = null;
                reminderPreviewToken = 0;
                if (reminderPreviewFilename) reminderPreviewFilename.textContent = '';
                if (reminderPreviewContent) reminderPreviewContent.innerHTML = '<div style="font-size: 48px; color: #95a5a6; margin-bottom: 10px;">📄</div>';
            }
        };
        
        // Load initial data with reset=true to ensure fresh load
        console.log('[INIT] Loading initial data...');
        const results = await Promise.allSettled([
            loadFiles(true),
            loadPrompts(true),
            loadReminders(true),
            loadGroups(true),
            loadAllGroups()
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
                    activePromptGroupId = null;
                    activeConversationId = null;
                    currentConversationHistory = [];
                    promptSelectedFiles = [];
                    window.promptSelectedFiles = [];
                    updateActiveGroupStyles();
                    // Clear the conversation view
                    const responseContent = document.getElementById('response-content');
                    if (responseContent) {
                        responseContent.innerHTML = '<span style="color: #2f3a3f;">Enter a message and click Send to get a response</span>';
                    }
                    // Refresh all visible tabs so stale data is removed from UI
                    await Promise.all([
                        loadFiles(true),
                        loadPrompts(true),
                        loadGroups(true),
                        loadReminders(true),
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
