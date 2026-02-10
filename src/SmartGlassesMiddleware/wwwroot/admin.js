const API_BASE = '/api/admin';
const themeStorageKey = 'uiTheme';

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

// Enhanced fetch with retry logic and cache busting
async function fetchWithRetry(url, options = {}, retries = 3) {
    // Add cache busting parameter and no-cache headers
    const cacheBuster = `_=${Date.now()}_${Math.random().toString(36).substring(7)}`;
    const separator = url.includes('?') ? '&' : '?';
    const bustUrl = `${url}${separator}${cacheBuster}`;
    
    // Force no-cache headers
    const fetchOptions = {
        ...options,
        headers: {
            ...options.headers,
            'Cache-Control': 'no-cache, no-store, must-revalidate',
            'Pragma': 'no-cache',
            'Expires': '0'
        },
        cache: 'no-store'
    };
    
    for (let i = 0; i < retries; i++) {
        try {
            console.log(`[API] Request: ${fetchOptions.method || 'GET'} ${url}${i > 0 ? ` (retry ${i})` : ''}`);
            const response = await fetch(bustUrl, fetchOptions);
            console.log(`[API] Response: ${response.status} ${response.statusText} for ${url}`);
            
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
}

// Helper to safely encode attached files array into a URL-safe JSON string
function encodeFilesData(msgFiles) {
    if (!Array.isArray(msgFiles) || msgFiles.length === 0) return '';
    try {
        return encodeURIComponent(JSON.stringify(msgFiles.map(f => (
            typeof f === 'string' ? { name: f } : { name: f.name || f.fileName || 'Document', id: f.id || f.documentId }
        ))));
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
    try {
        localStorage.setItem(themeStorageKey, theme);
    } catch {
        // ignore storage errors
    }
}

// Loading indicator
function showLoading(listId) {
    const list = document.getElementById(listId);
    const loading = document.createElement('div');
    loading.className = 'loading-indicator';
    loading.id = `${listId}-loading`;
    loading.innerHTML = '<p style="text-align: center; padding: 20px; color: #95a5a6;">Loading...</p>';
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
            list.innerHTML = '<div class="empty-state"><p>No files uploaded yet</p></div>';
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
                            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:4px;">
                                <span class="prompt-name" style="font-weight:600;color:#2c3e50;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:70%;">${displayName}</span>
                                <span class="badge" style="background:#3498db;color:white;padding:2px 6px;border-radius:10px;font-size:11px;min-width:20px;text-align:center;">${messageCount}</span>
                            </div>
                            <div style="font-size:11px;color:#95a5a6;margin-bottom:4px;">${timestamp}</div>
                            <div style="color:#666;font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${escapeHtml(String(lastMsg || 'No messages yet'))}</div>
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

                    fragment.appendChild(div);
                } catch (err) {
                    console.error('Error creating sidebar item', err);
                }
            });
            if (list) list.appendChild(fragment);

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
    const label = getProp(file, 'label') || 'Untitled';
    const fileName = getProp(file, 'fileName') || 'unknown';
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

document.getElementById('upload-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fileInput = document.getElementById('file-input');
    const label = document.getElementById('file-label').value;
    
    const formData = new FormData();
    formData.append('file', fileInput.files[0]);
    formData.append('label', label);
    
    try {
        const response = await fetch(`${API_BASE}/files`, {
            method: 'POST',
            body: formData
        });
        
        if (response.ok) {
            showMessage('File uploaded successfully');
            e.target.reset();
            // Force refresh files and all groups
            await loadFiles(true);
            await loadAllGroups(true);
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error uploading file', 'error');
        }
    } catch (error) {
        showMessage('Error uploading file: ' + error.message, 'error');
    }
});

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
    
    previewContent.innerHTML = '<p style="color: #95a5a6;">Loading preview...</p>';
    previewContainer.style.display = 'block';
    
    // Scroll to preview
    previewContainer.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    
    const cacheBuster = `${Date.now()}_${Math.random().toString(36).substring(7)}`;
    const previewUrl = `${API_BASE}/files/${id}/preview?v=${cacheBuster}`;
    const ext = fileName.split('.').pop().toLowerCase();
    
    if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg'].includes(ext)) {
        const img = new Image();

        hideLoading('prompts-list');
        state.prompts.loading = false;
        showMessage('Error loading prompts: ' + error.message, 'error');
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
        autoSaveCurrentConversation('switch');
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
            ? `${msgFiles.length} file(s) attached: ${msgFiles.map(f => (typeof f === 'string' ? f : f.name || f.fileName || 'Document')).join(', ')}`
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
                    const linkStyle = documentId ? 'color: #3498db; text-decoration: underline;' : 'color: #95a5a6; text-decoration: none;';
                    return `
                        <div style="display: flex; align-items: center; gap: 8px; font-size: 12px;">
                            <a href="${href}" target="_blank" style="${linkStyle}" onclick="${documentId ? '' : 'return false;'}">${escapeHtml(label)}</a>
                            ${documentId ? `<button type=\"button\" onclick=\"previewDocumentFile('${documentId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')\" style=\"padding: 2px 6px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 11px;\">Preview</button>` : ''}
                        </div>
                    `;
                }).join('')}
            </div>
        ` : '';

        return `
            <div style="margin-bottom: 12px;">
                <div style="color: ${msg.role === 'user' ? '#3498db' : '#27ae60'}; font-weight: 600; margin-bottom: 4px; display: flex; align-items: center; gap: 8px;">
                    <span>${msg.role === 'user' ? 'User' : 'Bot'}:</span>
                    ${msgFiles.length > 0 ? `<button type="button" onclick="showAttachedFilesModal('${filesData}')" style="background: none; border: none; font-size: 16px; color: #3498db; cursor: pointer; padding: 0;" title="${escapeHtml(filesTitle)}">📎</button>` : ''}
                </div>
                <div style="color: #555; padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
                ${msg.role === 'bot' ? msgSourcesHtml : ''}
            </div>
        `;
    }).join('');
    
    // Show attached files in a scrollable box
    const normalizedFiles = files.map(f => {
        if (typeof f === 'string') {
            return { name: f };
        }
        return { name: f.name || f.fileName || 'Document', id: f.id || f.documentId };
    });

    const filesFromMessages = messages.flatMap(msg => {
        if (!Array.isArray(msg.files)) return [];
        return msg.files.map(f => {
            if (typeof f === 'string') {
                return { name: f };
            }
            return { name: f.name || f.fileName || 'Document', id: f.id || f.documentId };
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
                        <div style="padding: 4px 0; color: #555; display: flex; align-items: center; justify-content: space-between; gap: 8px;">
                            <span>📎 ${escapeHtml(fileName)}</span>
                            <button type="button" onclick="previewDocumentFile('${fileId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')" style="padding: 2px 8px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;">Preview</button>
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
    
    // Reset response section
    const responseContent = document.getElementById('response-content');
    if (responseContent) {
        responseContent.innerHTML = '<span style="color: #999;">Enter a message and click Send to get a response</span>';
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
        const responseContent = document.getElementById('response-content');
        if (responseContent) {
            responseContent.innerHTML = '<span style="color: #999;">Loading conversation...</span>';
        }

        const res = await fetchWithRetry(`/api/conversations/${conversationId}`);
        if (!res.ok) {
            throw new Error(`HTTP ${res.status}: ${res.statusText}`);
        }

        const conversation = await res.json().catch(() => null);
        const messages = (conversation && (conversation.messages || conversation.Messages)) || [];

        currentConversationHistory = Array.isArray(messages)
            ? messages.map(m => ({
                role: (m.role || m.Role || '').toLowerCase() === 'assistant' ? 'bot' : (m.role || m.Role || 'user').toLowerCase(),
                content: m.content || m.Content || ''
            }))
            : [];

        const promptIdInput = document.getElementById('prompt-id');
        if (promptIdInput) promptIdInput.value = '';
        const promptTitle = document.getElementById('prompt-form-title');
        if (promptTitle) promptTitle.textContent = title || 'Conversation';

        if (currentConversationHistory.length === 0) {
            if (responseContent) {
                responseContent.innerHTML = '<span style="color: #999;">No messages yet</span>';
            }
            return;
        }

        const conversationHTML = currentConversationHistory.map(msg => {
            return `
                <div style="margin-bottom: 12px;">
                    <div style="color: ${msg.role === 'user' ? '#3498db' : '#27ae60'}; font-weight: 600; margin-bottom: 4px; display: flex; align-items: center; gap: 8px;">
                        <span>${msg.role === 'user' ? 'User' : 'Bot'}:</span>
                    </div>
                    <div style="color: #555; padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
                </div>
            `;
        }).join('');

        if (responseContent) {
            responseContent.innerHTML = `
                <div style="max-height: 500px; overflow-y: auto; padding: 15px; background: #f8f9fa; border-radius: 8px;">
                    ${conversationHTML}
                </div>
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
    try {
        const raw = localStorage.getItem(draftStorageKey);
        if (!raw) return false;
        const draft = JSON.parse(raw);
        if (!draft || (!Array.isArray(draft.messages) && !Array.isArray(draft.files))) return false;

        currentConversationHistory = Array.isArray(draft.messages) ? draft.messages : [];
        promptSelectedFiles = Array.isArray(draft.files) ? draft.files.map(f => ({
            name: f.name || 'Document',
            documentId: f.id || f.documentId,
            selected: false
        })) : [];
        window.promptSelectedFiles = promptSelectedFiles;
        updatePromptFilesList();

        if (currentConversationHistory.length > 0) {
            const conversationHTML = currentConversationHistory.map(msg => {
                const msgFiles = Array.isArray(msg.files) ? msg.files : [];
                const filesData = encodeFilesData(msgFiles);
                const filesTitle = msgFiles.length > 0
                    ? `${msgFiles.length} file(s) attached: ${msgFiles.map(f => (typeof f === 'string' ? f : f.name || f.fileName || 'Document')).join(', ')}`
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
                            const linkStyle = documentId ? 'color: #3498db; text-decoration: underline;' : 'color: #95a5a6; text-decoration: none;';
                            return `
                                <div style="display: flex; align-items: center; gap: 8px; font-size: 12px;">
                                    <a href="${href}" target="_blank" style="${linkStyle}" onclick="${documentId ? '' : 'return false;'}">${escapeHtml(label)}</a>
                                    ${documentId ? `<button type=\"button\" onclick=\"previewDocumentFile('${documentId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')\" style=\"padding: 2px 6px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 11px;\">Preview</button>` : ''}
                                </div>
                            `;
                        }).join('')}
                    </div>
                ` : '';

                return `
                    <div style="margin-bottom: 12px;">
                        <div style="color: ${msg.role === 'user' ? '#3498db' : '#27ae60'}; font-weight: 600; margin-bottom: 4px; display: flex; align-items: center; gap: 8px;">
                            <span>${msg.role === 'user' ? 'User' : 'Bot'}:</span>
                            ${msgFiles.length > 0 ? `<button type=\"button\" onclick=\"showAttachedFilesModal('${filesData}')\" style=\"background: none; border: none; font-size: 16px; color: #3498db; cursor: pointer; padding: 0;\" title=\"${escapeHtml(filesTitle)}\">📎</button>` : ''}
                        </div>
                        <div style="color: #555; padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
                        ${msg.role === 'bot' ? msgSourcesHtml : ''}
                    </div>
                `;
            }).join('');

            const responseContent = document.getElementById('response-content');
            if (responseContent) {
                responseContent.innerHTML = `
                    <div style="max-height: 500px; overflow-y: auto; padding: 15px; background: #f8f9fa; border-radius: 8px;">
                        ${conversationHTML}
                    </div>
                `;
            }
        }

        return true;
    } catch (error) {
        console.warn('[AUTO SAVE] Failed to restore draft:', error);
        return false;
    }
}

function closeAutoGroupView() {
    document.querySelectorAll('.sidebar-prompt-item').forEach(item => item.classList.remove('active'));
    const promptIdInput = document.getElementById('prompt-id');
    if (promptIdInput) promptIdInput.value = '';
    const responseContent = document.getElementById('response-content');
    if (responseContent) {
        responseContent.innerHTML = '<span style="color: #999;">Enter a prompt and click Send to get a response</span>';
    }
}

function updatePromptFilesList() {
    const filesList = document.getElementById('prompt-files-list');
    const promptFilesIcon = document.getElementById('prompt-files-icon');
    
    if (!filesList) return;
    
    // Update paperclip icon color
    if (promptFilesIcon) {
        if (promptSelectedFiles.length === 0) {
            promptFilesIcon.style.color = '#95a5a6'; // Gray when no files
        } else {
            promptFilesIcon.style.color = '#3498db'; // Blue when files present
        }
    }
    
    if (promptSelectedFiles.length === 0) {
        filesList.innerHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No files to show</div>';
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
                    <button type="button" onclick="event.stopPropagation(); previewDocumentFile('${fileId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')" style="padding: 4px 10px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;">
                        Preview
                    </button>
                </div>
            `;
        }).join('');
    }
}

function updateActiveGroupStyles() {
    document.querySelectorAll('[data-group-card="true"]').forEach(card => {
        const groupId = card.dataset.groupId;
        const isActive = !!activePromptGroupId && groupId === String(activePromptGroupId);
        const badge = card.querySelector('[data-group-active-badge="true"]');
        const input = card.querySelector('input[data-group-id]');

        card.style.background = isActive ? '#eaf4ff' : '#f5f5f5';
        card.style.border = isActive ? '2px solid #3498db' : '2px solid transparent';
        if (input) {
            input.style.background = isActive ? '#ffffff' : 'white';
            input.style.borderColor = isActive ? '#3498db' : '#ddd';
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
                    ? `${msgFiles.length} file(s) attached: ${msgFiles.map(f => (typeof f === 'string' ? f : f.name || f.fileName || 'Document')).join(', ')}`
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
                            const linkStyle = documentId ? 'color: #3498db; text-decoration: underline;' : 'color: #95a5a6; text-decoration: none;';
                            return `
                                <div style="display: flex; align-items: center; gap: 8px; font-size: 12px;">
                                    <a href="${href}" target="_blank" style="${linkStyle}" onclick="${documentId ? '' : 'return false;'}">${escapeHtml(label)}</a>
                                    ${documentId ? `<button type=\"button\" onclick=\"previewDocumentFile('${documentId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')\" style=\"padding: 2px 6px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 11px;\">Preview</button>` : ''}
                                </div>
                            `;
                        }).join('')}
                    </div>
                ` : '';

                return `
                    <div style="margin-bottom: 12px;">
                        <div style="color: ${msg.role === 'user' ? '#3498db' : '#27ae60'}; font-weight: 600; margin-bottom: 4px; display: flex; align-items: center; gap: 8px;">
                            <span>${msg.role === 'user' ? 'User' : 'Bot'}:</span>
                            ${msgFiles.length > 0 ? `<button type=\"button\" onclick=\"showAttachedFilesModal('${filesData}')\" style=\"background: none; border: none; font-size: 16px; color: #3498db; cursor: pointer; padding: 0;\" title=\"${escapeHtml(filesTitle)}\">📎</button>` : ''}
                        </div>
                        <div style="color: #555; padding-left: 12px; white-space: pre-wrap;">${escapeHtml(msg.content)}</div>
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
        <div style="margin-top: 10px; color: #999; font-size: 0.85rem;">
            Click "Track" to monitor this prompt's usage
        </div>
    `;
}

function hidePromptResponse() {
    const responseContent = document.getElementById('response-content');
    responseContent.innerHTML = '<span style="color: #999;">Click Track to monitor prompt responses</span>';
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
            <div style="color: #3498db; font-size: 1.2rem; margin-bottom: 10px;">📊 Tracking Active</div>
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
        const response = await fetchWithRetry(`${API_BASE}/groups?page=${state.groups.page}&pageSize=${state.groups.pageSize}`);
        
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        
        const result = await response.json();
        console.log('Groups API response:', result);
        // Handle both lowercase and uppercase from API
        const groupsData = result.data || result.Data || [];
        const pagination = result.pagination || result.Pagination || {};
        console.log('Groups data length:', groupsData.length);
        
        // Check for and delete empty groups
        const emptyGroups = groupsData.filter(group => {
            const files = getProp(group, 'files') || [];
            const prompts = getProp(group, 'prompts') || [];
            const conversations = getProp(group, 'conversations') || [];
            const fileCount = Array.isArray(files) ? files.length : 0;
            const promptCount = Array.isArray(prompts) ? prompts.length : 0;
            const conversationCount = Array.isArray(conversations) ? conversations.length : 0;
            return fileCount === 0 && promptCount === 0 && conversationCount === 0;
        });
        
        // Auto-delete empty groups in parallel
        if (emptyGroups.length > 0) {
            console.log(`[AUTO-DELETE] Found ${emptyGroups.length} empty groups, deleting...`);
            const deletePromises = emptyGroups.map(async (group) => {
                const id = getProp(group, 'id');
                const name = getProp(group, 'name') || 'Untitled';
                console.log(`[AUTO-DELETE] Deleting empty group: ${name} (${id})`);
                try {
                    const deleteRes = await fetch(`${API_BASE}/groups/${id}`, { method: 'DELETE' });
                    if (deleteRes.ok || deleteRes.status === 204) {
                        console.log(`[AUTO-DELETE] Successfully deleted empty group: ${name}`);
                        return id;
                    } else {
                        console.warn(`[AUTO-DELETE] Failed to delete empty group: ${name}`, deleteRes.status);
                        return null;
                    }
                } catch (err) {
                    console.error(`[AUTO-DELETE] Error deleting empty group: ${name}`, err);
                    return null;
                }
            });
            
            const deletedIds = (await Promise.all(deletePromises)).filter(id => id !== null);
            const deletedIdsSet = new Set(deletedIds);
            
            // Filter out deleted groups using Set for O(1) lookup
            const validGroups = groupsData.filter(g => !deletedIdsSet.has(getProp(g, 'id')));
            groupsData.length = 0;
            groupsData.push(...validGroups);
        }
        
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
    const fileCount = Array.isArray(files) ? files.length : 0;

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
    
    return div;
}

function createGroupCard(group) {
    const div = document.createElement('div');
    const id = getProp(group, 'id');
    const name = getProp(group, 'name') || 'Untitled';
    const description = getProp(group, 'description') || getProp(group, 'Description') || '';
    const isActive = !!activePromptGroupId && String(activePromptGroupId) === String(id);
    
    // Skip if this is a reminder (will be shown in Reminders tab)
    if (description && description.toLowerCase() === 'reminder') {
        return null;
    }
    
    // Get counts for files, conversations, and reminders
    const files = getProp(group, 'files') || [];
    const conversations = getProp(group, 'conversations') || [];
    const reminders = getProp(group, 'reminders') || [];
    const fileCount = Array.isArray(files) ? files.length : 0;
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
            <span title="${fileCount} file(s)"><span aria-hidden="true">📄</span> <span aria-label="${fileCount} files">${fileCount} files</span></span>
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
                    formData.append('label', fileObj.name); // Add required label
                    
                    try {
                        console.log('[CREATE GROUP] Uploading file:', fileObj.name);
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
                    const convRes = await fetch(`${API_BASE}/conversations`, {
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
                    formData.append('label', fileObj.name);
                    
                    try {
                        console.log('[CREATE REMINDER] Uploading file:', fileObj.name);
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
            } else {
                console.log('[CREATE REMINDER] No files to upload');
            }
            
            showMessage(id ? 'Reminder updated successfully' : 'Reminder created successfully');
            document.getElementById('reminder-form').reset();
            document.getElementById('reminder-id').value = '';
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
        const convRes = await fetch(`${API_BASE}/conversations?groupId=${encodeURIComponent(id)}`);
        if (!convRes.ok) {
            showMessage('Failed to load conversations for group', 'error');
            return;
        }

        const convJson = await convRes.json();
        const conversations = convJson || [];

        // 2) Permanently delete each conversation via the permanent delete API
        if (conversations.length > 0) {
            showMessage(`Deleting ${conversations.length} conversations...`);
            const deletePromises = conversations.map(c =>
                fetch(`${API_BASE}/conversations/${c.id}`, { method: 'DELETE' })
                    .then(r => ({ ok: r.ok, status: r.status, id: c.id }))
                    .catch(e => ({ ok: false, error: e.message, id: c.id }))
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
            console.log('[MANAGE] Fetching all files and prompts for manage resources...');
            console.log('[MANAGE] Fetching from:', `${API_BASE}/files?page=1&pageSize=1000`);
            console.log('[MANAGE] Fetching from:', `${API_BASE}/prompts?page=1&pageSize=1000`);
            
            const [filesRes, promptsRes] = await Promise.all([
                fetchWithRetry(`${API_BASE}/files?page=1&pageSize=1000`),
                fetchWithRetry(`${API_BASE}/prompts?page=1&pageSize=1000`)
            ]);
            
            console.log('[MANAGE] Files response status:', filesRes.status);
            console.log('[MANAGE] Prompts response status:', promptsRes.status);
            
            if (filesRes.ok) {
                const filesResult = await filesRes.json();
                console.log('[MANAGE] Files response:', filesResult);
                // Handle both lowercase 'data' and uppercase 'Data' from API
                allFiles = filesResult.data || filesResult.Data || [];
                console.log('[MANAGE] Fetched files count:', allFiles.length);
                if (allFiles.length > 0) {
                    console.log('[MANAGE] First file:', allFiles[0]);
                }
            } else {
                const errorText = await filesRes.text();
                console.error('[MANAGE] Failed to load files:', filesRes.status, errorText);
            }
            
            if (promptsRes.ok) {
                const promptsResult = await promptsRes.json();
                console.log('[MANAGE] Prompts response:', promptsResult);
                // Handle both lowercase 'data' and uppercase 'Data' from API
                allPrompts = promptsResult.data || promptsResult.Data || [];
                console.log('[MANAGE] Fetched prompts count:', allPrompts.length);
                if (allPrompts.length > 0) {
                    console.log('[MANAGE] First prompt:', allPrompts[0]);
                }
            } else {
                const errorText = await promptsRes.text();
                console.error('[MANAGE] Failed to load prompts:', promptsRes.status, errorText);
            }
        } catch (fetchError) {
            console.error('[MANAGE] Error fetching resources:', fetchError);
            showMessage('Error loading resources: ' + fetchError.message, 'error');
        }
        
        console.log('[MANAGE] Final counts - Files:', allFiles.length, 'Prompts:', allPrompts.length);
        
        if (allFiles.length === 0 && allPrompts.length === 0) {
            console.warn('[MANAGE] WARNING: No files or prompts returned from API');
        }
        
        // Use getProp to access files and prompts arrays with case-insensitive fallback
        const groupFiles = getProp(currentGroup, 'files') || [];
        const groupPrompts = getProp(currentGroup, 'prompts') || [];
        const groupConversations = getProp(currentGroup, 'conversations') || [];
        const groupReminders = getProp(currentGroup, 'reminders') || [];
        const groupFileIds = new Set(groupFiles.map(f => getProp(f, 'id')));
        const groupPromptIds = new Set(groupPrompts.map(p => getProp(p, 'id')));
        
        console.log('[MANAGE] Group files:', groupFiles);
        console.log('[MANAGE] Group conversations:', groupConversations);
        console.log('[MANAGE] Group reminders:', groupReminders);
        
        const modalBody = document.getElementById('modal-content-body');

        // Build files list HTML
        let filesListHTML = '';
        if (groupFiles.length === 0) {
            filesListHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No files attached yet</div>';
        } else {
            filesListHTML = groupFiles.map(file => {
                const fileName = getProp(file, 'originalFileName') || getProp(file, 'fileName') || 'Unknown';
                const fileId = getProp(file, 'id');
                return `
                    <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0;">
                        <span style="font-size: 14px; color: #333; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                            ${escapeHtml(fileName)}
                        </span>
                        <button type="button" onclick="previewReminderFile('${fileId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')"
                            style="padding: 4px 12px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; white-space: nowrap;">
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
                    <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0;">
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
                    <div style="border: 2px solid #3498db; border-radius: 8px; padding: 12px; display: flex; flex-direction: column; position: relative;">
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
        modalContent.innerHTML = `<embed src="${previewUrl}" type="application/pdf" width="100%" height="55vh" style="border-radius: 4px; min-height: 400px;">`;
    } else if (['txt', 'json', 'xml', 'csv', 'md'].includes(ext)) {
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
    } else {
        modalContent.innerHTML = `
            <div style="text-align: center; padding: 40px;">
                <div style="font-size: 64px; margin-bottom: 20px;">📄</div>
                <p style="color: #7f8c8d; font-size: 14px;">Preview not available for this file type.</p>
                <p style="color: #95a5a6; font-size: 12px; margin-top: 10px;">File: ${escapeHtml(fileName)}</p>
            </div>
        `;
    }
}

let documentsCache = null;

async function resolveDocumentIdByName(fileName) {
    if (!fileName) return null;
    if (!documentsCache) {
        try {
            const response = await fetch('/api/documents?page=1&pageSize=1000');
            if (!response.ok) return null;
            const data = await response.json();
            const docs = data.documents || data.Documents || [];
            documentsCache = docs.reduce((acc, doc) => {
                if (doc.fileName) {
                    acc[doc.fileName.toLowerCase()] = doc.id || doc.Id;
                }
                return acc;
            }, {});
        } catch {
            return null;
        }
    }

    return documentsCache[fileName.toLowerCase()] || null;
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
        modalContent.innerHTML = `<embed src="${previewUrl}" type="application/pdf" width="100%" height="55vh" style="border-radius: 4px; min-height: 400px;">`;
    } else if (['txt', 'json', 'xml', 'csv', 'md'].includes(ext)) {
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
    } else {
        modalContent.innerHTML = `
            <div style="text-align: center; padding: 40px;">
                <div style="font-size: 64px; margin-bottom: 20px;">📄</div>
                <p style="color: #7f8c8d; font-size: 14px;">Preview not available for this file type.</p>
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
                        <button type="button" onclick="previewDocumentFile('${fileId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')" style="padding: 4px 10px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;">
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
    
    // Wait for API to be ready
    const apiReady = await waitForAPI();
    if (!apiReady) {
        showMessage('API is not responding. Please refresh the page.', 'error');
        alert('ERROR: The backend API is not responding. Please wait a moment and refresh the page.');
        return;
    }
    
    try {
        // Apply saved theme
        const savedTheme = localStorage.getItem(themeStorageKey) || 'light';
        applyTheme(savedTheme);
        document.getElementById('theme-toggle')?.addEventListener('click', () => {
            const nextTheme = document.body.classList.contains('dark') ? 'light' : 'dark';
            applyTheme(nextTheme);
        });

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
        
        // Setup prompts file management
        const promptFileInput = document.getElementById('prompt-file-input');
        const promptFilesList = document.getElementById('prompt-files-list');
        const promptFilesIcon = document.getElementById('prompt-files-icon');
        const promptFileSection = document.getElementById('prompt-file-section');
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
        
        updatePromptFilesList();
        
        // Show file section when clicking "add file+" button
        document.getElementById('prompt-show-file-btn')?.addEventListener('click', () => {
            const isCurrentlyHidden = promptFileSection.style.display === 'none' || !promptFileSection.style.display;
            if (isCurrentlyHidden) {
                promptFileSection.style.display = 'block';
                document.getElementById('prompt-preview-panel').style.display = 'block';
                document.getElementById('prompt-files-panel').style.display = 'flex';
            } else {
                promptFileSection.style.display = 'none';
            }
        });
        
        // Show only Files panel when clicking paperclip in Prompts
        promptFilesIcon?.addEventListener('click', () => {
            const filesPanel = document.getElementById('prompt-files-panel');
            
            // Hide preview panel, show only files panel
            document.getElementById('prompt-preview-panel').style.display = 'none';
            filesPanel.style.display = 'flex';
            filesPanel.setAttribute('data-context', 'prompts');
            
            promptFileSection.style.display = 'block';
            
            // Update files list after showing the panel
            updatePromptFilesList();
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
                    } else {
                        promptPreviewContent.innerHTML = `
                            <div style="text-align: center;">
                                <div style="font-size: 64px; margin-bottom: 10px;">📄</div>
                                <div style="color: #7f8c8d; font-size: 12px;">Preview not available</div>
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
                        
                        // Upload file to server with progress tracking
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
                        
                        xhr.open('POST', '/api/documents/upload');
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
                        updatePromptFilesList();
                        showMessage(`File "${file.name}" uploaded successfully`, 'success');
                        
                        // Clear preview
                        promptPreviewFile = null;
                        if (promptPreviewFilename) promptPreviewFilename.textContent = '';
                        if (promptPreviewContent) promptPreviewContent.innerHTML = `<div style="font-size: 48px; color: #95a5a6;">📄</div>`;
                        if (promptFileInput) promptFileInput.value = '';
                        
                        // Reload files list to show the new file
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
        
        // Send button function - sends prompt to API
        const buildSourcesHtml = (sources) => {
            if (!sources || sources.length === 0) return '';
            return `
                <div style="margin-top: 15px; padding: 10px; background: white; border: 1px solid #dee2e6; border-radius: 4px;">
                    <div style="font-weight: 600; color: #2c3e50; margin-bottom: 8px; font-size: 0.9rem;">Sources Used:</div>
                    <div style="display: flex; flex-direction: column; gap: 6px;">
                        ${sources.map(source => {
                            const documentId = source.documentId || source.DocumentId;
                            const fileName = source.fileName || source.FileName || 'Document';
                            const page = source.pageNumber || source.PageNumber;
                            const chunkText = source.chunkText || source.ChunkText || '';
                            const label = page ? `${fileName} (p.${page})` : fileName;
                            const href = documentId ? `/api/documents/${documentId}/download` : '#';
                            const linkStyle = documentId ? 'color: #3498db; text-decoration: underline;' : 'color: #95a5a6; text-decoration: none;';
                            return `
                                <div style="display: flex; flex-direction: column; gap: 4px;">
                                    <div style="display: flex; align-items: center; gap: 8px;">
                                        <a href="${href}" target="_blank" style="${linkStyle}" onclick="${documentId ? '' : 'return false;'}">${escapeHtml(label)}</a>
                                        ${documentId ? `<button type=\"button\" onclick=\"previewDocumentFile('${documentId}', '${escapeHtml(fileName).replace(/'/g, "\\'")}')\" style=\"padding: 2px 8px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;\">Preview</button>` : ''}
                                    </div>
                                    ${chunkText ? `<div style=\"font-size: 12px; color: #666; padding-left: 4px;\">${escapeHtml(chunkText)}</div>` : ''}
                                </div>
                            `;
                        }).join('')}
                    </div>
                </div>
            `;
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
                // Show loading state and cancel button
                responseContent.innerHTML = `
                    <div style="text-align: center; padding: 20px; color: #3498db;">
                        <div style="font-size: 1.2rem; margin-bottom: 10px;">⏳ Sending...</div>
                        <div style="font-size: 0.9rem; color: #666;">Processing your request...</div>
                    </div>
                `;
                
                // Show cancel button, hide send button
                if (sendBtn) sendBtn.style.display = 'none';
                if (cancelBtn) cancelBtn.style.display = 'block';
                
                // Send query to documents endpoint. Use server-expected property names (Query/TopK)
                const documentsUrl = '/api/documents/query';
                const selectedFiles = promptSelectedFiles.some(f => f.selected)
                    ? promptSelectedFiles.filter(f => f.selected)
                    : promptSelectedFiles;
                const documentIds = selectedFiles
                    .map(f => f.documentId || f.id)
                    .filter(Boolean);
                const includeAllGroups = document.getElementById('prompt-include-all')?.checked;
                const payload = { Query: promptText, TopK: 5 };
                if (documentIds.length > 0) {
                    payload.DocumentIds = documentIds;
                }
                if (activePromptGroupId && !includeAllGroups) {
                    payload.GroupIds = [activePromptGroupId];
                }
                console.log('[SEND PROMPT] Sending documents query to', documentsUrl, 'payload:', payload);

                currentRequestController = new AbortController();
                const timeoutMs = 300000; // 300 seconds (5 minutes) - increased timeout
                const timeoutId = setTimeout(() => currentRequestController.abort(), timeoutMs);

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
                    // Server will persist group->conversation mapping. If we don't have an active group,
                    // ask the server for the conversation to determine the associated group and activate it.
                    if (!activePromptGroupId) {
                        await syncActiveGroupFromConversation(responseConversationId);
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
                                ${msg.files && msg.files.length > 0 ? `<button type="button" onclick="showAttachedFilesModal('${filesData}')" style="background: none; border: none; font-size: 16px; color: #3498db; cursor: pointer; padding: 0;" title="${escapeHtml(filesTitle)}">📎</button>` : ''}
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

                // Persist conversation snapshot to server so it survives reloads
                try {
                    await autoSaveCurrentConversation('send');
                } catch (err) {
                    console.warn('[SEND PROMPT] Auto-save after send failed:', err);
                }

                // Clear attached files after sending
                promptSelectedFiles = [];
                window.promptSelectedFiles = promptSelectedFiles;
                updatePromptFilesList();
                try {
                    localStorage.removeItem(draftStorageKey);
                } catch {
                    // ignore
                }
                
            } catch (error) {
                console.error('Error sending prompt:', error);
                if (error.name === 'AbortError') {
                    responseContent.innerHTML = `
                        <div style="text-align: center; padding: 20px; color: #e67e22;">
                            <div style="font-size: 1.2rem; margin-bottom: 10px;">🚫 Cancelled</div>
                            <div style="font-size: 0.9rem;">Request was cancelled.</div>
                        </div>
                    `;
                    showMessage('Request cancelled', 'info');
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
            } finally {
                // Reset buttons regardless of success or error
                currentRequestController = null;
                if (sendBtn) sendBtn.style.display = 'block';
                if (cancelBtn) cancelBtn.style.display = 'none';
            }
        };
        
        // Send button click handler
        document.getElementById('prompt-send-btn')?.addEventListener('click', sendPrompt);
        
        // Cancel button click handler
        document.getElementById('prompt-cancel-btn')?.addEventListener('click', () => {
            if (currentRequestController) {
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

        document.getElementById('prompt-name')?.addEventListener('input', () => {
            autoSaveCurrentConversation('input');
        });
        
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
                await autoSaveCurrentConversation('new_chat');
                showMessage('Conversation saved to Auto Groups', 'success');
                
                // Reset for new chat
                currentConversationHistory = [];
                promptSelectedFiles = [];
                window.promptSelectedFiles = promptSelectedFiles;
                updatePromptFilesList();
                
                const promptIdInput = document.getElementById('prompt-id');
                if (promptIdInput) promptIdInput.value = '';
                document.getElementById('prompt-name').value = '';
                document.getElementById('response-content').innerHTML = '<span style="color: #999;">Enter a prompt and click Send to get a response</span>';
                
                // Reload prompts list to show new Auto Group
                loadPrompts(true);
                
            } catch (error) {
                console.error('Error saving conversation:', error);
                showMessage('Failed to save conversation', 'error');
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
                    groupFilesIcon.style.color = '#3498db'; // Blue when files present
                }
            }
            
            // Update files list in Files section with checkboxes (only show if files exist)
            if (groupFilesList) {
                if (groupSelectedFiles.length === 0) {
                    groupFilesList.innerHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No files attached yet</div>';
                } else {
                    groupFilesList.innerHTML = groupSelectedFiles.map((fileObj, index) => `
                        <div style="padding: 12px 16px; display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #e0e0e0; ${fileObj.selected ? 'background: #f0f0f0;' : ''}">
                            <input type="checkbox" ${fileObj.selected ? 'checked' : ''} style="width: 16px; height: 16px; cursor: pointer; margin: 0;" onclick="toggleGroupFileSelection(${index})">
                            <span onclick="toggleGroupFileSelection(${index})" style="font-size: 14px; color: #333; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; user-select: none;">
                                ${escapeHtml(fileObj.name)}
                            </span>
                            <button type="button" onclick="event.stopPropagation(); previewGroupFile(${index})" style="padding: 4px 12px; background: #3498db; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; white-space: nowrap;">Preview</button>
                        </div>
                    `).join('');
                }
            }
            
            // Sync window.groupSelectedFiles
            window.groupSelectedFiles = groupSelectedFiles;
            
            // Update dropdown list
            if (groupFilesListDropdown) {
                if (groupSelectedFiles.length === 0) {
                    groupFilesListDropdown.innerHTML = '<div style="color: #95a5a6; text-align: center; padding: 10px;">No files attached</div>';
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
        
        // Handle file selection - show preview
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
                    } else {
                        groupPreviewContent.innerHTML = `
                            <div style="text-align: center;">
                                <div style="font-size: 64px; margin-bottom: 10px;">📄</div>
                                <div style="color: #7f8c8d; font-size: 12px;">Preview not available</div>
                            </div>
                        `;
                    }
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
                        <p style="color: #7f8c8d; font-size: 14px;">Preview not available for this file type.</p>
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
                    reminderFilesIcon.style.color = '#3498db';
                }
            }
            
            if (reminderFilesList) {
                if (reminderSelectedFiles.length === 0) {
                    reminderFilesList.innerHTML = '<div style="color: #95a5a6; text-align: center; padding: 20px; font-size: 14px;">No files attached yet</div>';
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
        
        // Handle file selection - show preview
        if (reminderFileInput) {
            reminderFileInput.addEventListener('change', (e) => {
                const file = e.target.files[0];
                if (file) {
                    reminderPreviewFile = file;
                    reminderPreviewFilename.textContent = file.name;
                    
                    const ext = file.name.split('.').pop().toLowerCase();
                    if (['jpg', 'jpeg', 'png', 'gif'].includes(ext)) {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            reminderPreviewContent.innerHTML = `<img src="${e.target.result}" style="max-width: 100%; max-height: 200px; border-radius: 4px; object-fit: contain;">`;
                        };
                        reader.readAsDataURL(file);
                    } else if (ext === 'pdf') {
                        const reader = new FileReader();
                        reader.onload = function(e) {
                            reminderPreviewContent.innerHTML = `<embed src="${e.target.result}" type="application/pdf" width="100%" height="200px" style="border-radius: 4px;">`;
                        };
                        reader.readAsDataURL(file);
                    } else {
                        reminderPreviewContent.innerHTML = `
                            <div style="text-align: center;">
                                <div style="font-size: 64px; margin-bottom: 10px;">📄</div>
                                <div style="color: #7f8c8d; font-size: 12px;">Preview not available</div>
                            </div>
                        `;
                    }
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

        if (AUTO_SAVE_ENABLED) restoreDraftConversation();

        window.addEventListener('beforeunload', () => {
            autoSaveCurrentConversation('unload');
        });

        document.addEventListener('visibilitychange', () => {
            if (document.hidden) {
                autoSaveCurrentConversation('hidden');
            }
        });
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
