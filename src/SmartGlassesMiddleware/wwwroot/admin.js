const API_BASE = '/api/admin';

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

// Pagination state
const state = {
    files: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    prompts: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    groups: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
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
        } else if (tabName === 'groups') {
            loadGroups(true);
            loadAllGroups();
        }
    });
});

// Show message
function showMessage(message, type = 'success') {
    const container = document.getElementById('message-container');
    const div = document.createElement('div');
    div.className = `message ${type}`;
    div.textContent = message;
    container.appendChild(div);
    setTimeout(() => div.remove(), 5000);
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
            <button data-action="add-to-group-file" data-file-id="${id}" data-file-label="${escapeHtml(label)}">Add to Group</button>
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
    div.querySelector('[data-action="add-to-group-file"]').addEventListener('click', (e) => {
        showQuickAddToGroup(e.target.dataset.fileId, 'file', e.target.dataset.fileLabel);
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

async function previewFile(id, fileName) {
    const previewContainer = document.getElementById('file-preview');
    const previewContent = document.getElementById('preview-content');
    
    previewContent.innerHTML = '<p style="color: #95a5a6;">Loading preview...</p>';
    previewContainer.style.display = 'block';
    
    // Scroll to preview
    previewContainer.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    
    const fileUrl = `/api/media/file/${id}`;
    const ext = fileName.split('.').pop().toLowerCase();
    
    if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg'].includes(ext)) {
        previewContent.innerHTML = `<img src="${fileUrl}" alt="${escapeHtml(fileName)}" style="max-width: 100%; max-height: 500px; border-radius: 4px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">`;
    } else if (ext === 'pdf') {
        previewContent.innerHTML = `<embed src="${fileUrl}" type="application/pdf" width="100%" height="600px" style="border-radius: 4px;">`;
    } else {
        previewContent.innerHTML = `<p style="color: #e74c3c;">Preview not available for this file type.</p>`;
    }
}

document.getElementById('close-preview').addEventListener('click', () => {
    document.getElementById('file-preview').style.display = 'none';
});

// Prompts with lazy loading
async function loadPrompts(reset = false) {
    if (state.prompts.loading) return;
    if (!reset && !state.prompts.hasMore) return;
    
    if (reset) {
        state.prompts.page = 1;
        state.prompts.hasMore = true;
        state.prompts.data = [];
        document.getElementById('prompts-list').innerHTML = '';
    }
    
    state.prompts.loading = true;
    showLoading('prompts-list');
    
    try {
        const response = await fetchWithRetry(`${API_BASE}/prompts?page=${state.prompts.page}&pageSize=${state.prompts.pageSize}`);
        
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        
        const result = await response.json();
        console.log('Prompts API response:', result);
        // Handle both lowercase and uppercase from API
        const promptsData = result.data || result.Data || [];
        const pagination = result.pagination || result.Pagination || {};
        console.log('Prompts data length:', promptsData.length);
        
        const list = document.getElementById('prompts-list');
        
        hideLoading('prompts-list');
        
        if (promptsData && promptsData.length > 0) {
            state.prompts.data.push(...promptsData);
            
            const fragment = document.createDocumentFragment();
            promptsData.forEach(prompt => {
                const div = createPromptCard(prompt);
                fragment.appendChild(div);
            });
            list.appendChild(fragment);
            
            state.prompts.hasMore = pagination.hasNext || pagination.HasNext;
            state.prompts.page++;
        } else if (reset) {
            list.innerHTML = '<div class="empty-state"><p>No prompts created yet</p></div>';
        }
        
        state.prompts.loading = false;
    } catch (error) {
        hideLoading('prompts-list');
        state.prompts.loading = false;
        showMessage('Error loading prompts: ' + error.message, 'error');
    }
}

function createPromptCard(prompt) {
    const div = document.createElement('div');
    div.className = 'card';
    
    const id = getProp(prompt, 'id');
    const name = getProp(prompt, 'name') || 'Untitled';
    const promptText = getProp(prompt, 'promptText') || '';
    const createdAt = getProp(prompt, 'createdAt');
    const updatedAt = getProp(prompt, 'updatedAt');
    const groups = getProp(prompt, 'groups') || [];
    
    div.innerHTML = `
        <div class="card-header">
            <div class="card-title">${escapeHtml(name)}</div>
        </div>
        <div class="card-meta">
            Created: ${createdAt ? new Date(createdAt).toLocaleString() : 'Unknown'}
            ${updatedAt ? ` | Updated: ${new Date(updatedAt).toLocaleString()}` : ''}
        </div>
        <div style="margin-top: 10px; padding: 10px; background-color: #f8f9fa; border-radius: 4px; white-space: pre-wrap;">${escapeHtml(promptText)}</div>
        ${groups && groups.length > 0 ? `
            <div style="margin-top: 8px;">
                ${groups.map(g => `<span class="badge">${escapeHtml(getProp(g, 'name') || '')}</span>`).join('')}
            </div>
        ` : ''}
        <div class="card-actions">
            <button data-action="add-to-group-prompt" data-prompt-id="${id}" data-prompt-name="${escapeHtml(name)}">Add to Group</button>
            <button data-action="edit-prompt" data-prompt-id="${id}" data-prompt-name="${escapeHtml(name)}" data-prompt-text="${escapeHtml(promptText)}">Edit</button>
            <button class="delete" data-action="delete-prompt" data-prompt-id="${id}">Delete</button>
        </div>
    `;
    
    // Attach event listeners
    div.querySelector('[data-action="add-to-group-prompt"]').addEventListener('click', (e) => {
        showQuickAddToGroup(e.target.dataset.promptId, 'prompt', e.target.dataset.promptName);
    });
    div.querySelector('[data-action="edit-prompt"]').addEventListener('click', (e) => {
        editPrompt(e.target.dataset.promptId, e.target.dataset.promptName, e.target.dataset.promptText);
    });
    div.querySelector('[data-action="delete-prompt"]').addEventListener('click', (e) => {
        deletePrompt(e.target.dataset.promptId);
    });
    
    return div;
}

let isEditingPrompt = false;

document.getElementById('prompt-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const id = document.getElementById('prompt-id').value;
    const name = document.getElementById('prompt-name').value;
    const text = document.getElementById('prompt-text').value;
    
    const data = { name, promptText: text };
    
    try {
        const url = isEditingPrompt ? `${API_BASE}/prompts/${id}` : `${API_BASE}/prompts`;
        const method = isEditingPrompt ? 'PUT' : 'POST';
        
        const response = await fetch(url, {
            method,
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        
        if (response.ok) {
            showMessage(isEditingPrompt ? 'Prompt updated successfully' : 'Prompt created successfully');
            cancelEditPrompt();
            await loadPrompts(true);
            await loadAllGroups(true);
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error saving prompt', 'error');
        }
    } catch (error) {
        showMessage('Error saving prompt: ' + error.message, 'error');
    }
});

function editPrompt(id, name, text) {
    isEditingPrompt = true;
    document.getElementById('prompt-id').value = id;
    document.getElementById('prompt-name').value = name;
    document.getElementById('prompt-text').value = text;
    document.getElementById('prompt-submit-btn').textContent = 'Update Prompt';
    document.getElementById('prompt-cancel-btn').style.display = 'inline-block';
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function cancelEditPrompt() {
    isEditingPrompt = false;
    document.getElementById('prompt-form').reset();
    document.getElementById('prompt-id').value = '';
    document.getElementById('prompt-submit-btn').textContent = 'Add Prompt';
    document.getElementById('prompt-cancel-btn').style.display = 'none';
}

document.getElementById('prompt-cancel-btn').addEventListener('click', cancelEditPrompt);

async function deletePrompt(id) {
    if (!confirm('Are you sure you want to delete this prompt?')) return;
    
    try {
        const response = await fetch(`${API_BASE}/prompts/${id}`, {
            method: 'DELETE'
        });
        
        if (response.ok || response.status === 204) {
            showMessage('Prompt deleted successfully');
            await loadPrompts(true);
            await loadAllGroups(true);
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error deleting prompt', 'error');
        }
    } catch (error) {
        showMessage('Error deleting prompt: ' + error.message, 'error');
    }
}

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
        
        const list = document.getElementById('groups-list');
        
        hideLoading('groups-list');
        
        if (groupsData && groupsData.length > 0) {
            state.groups.data.push(...groupsData);
            
            const fragment = document.createDocumentFragment();
            groupsData.forEach(group => {
                const div = createGroupCard(group);
                fragment.appendChild(div);
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

function createGroupCard(group) {
    const div = document.createElement('div');
    div.className = 'card';
    
    const id = getProp(group, 'id');
    const name = getProp(group, 'name') || 'Untitled';
    const description = getProp(group, 'description') || '';
    const createdAt = getProp(group, 'createdAt');
    const files = getProp(group, 'files') || [];
    const prompts = getProp(group, 'prompts') || [];
    
    div.innerHTML = `
        <div class="card-header">
            <div class="card-title">${escapeHtml(name)}</div>
        </div>
        ${description ? `<div style="margin-bottom: 10px; color: #555;">${escapeHtml(description)}</div>` : ''}
        <div class="card-meta">Created: ${createdAt ? new Date(createdAt).toLocaleString() : 'Unknown'}</div>
        <div class="group-resources">
            <div class="group-resources-title">Files (${files.length}):</div>
            ${files && files.length > 0 ? files.map(f => `<span class="badge">${escapeHtml(getProp(f, 'label') || '')}</span>`).join('') : '<span style="color: #95a5a6; font-size: 0.85rem;">No files</span>'}
        </div>
        <div class="group-resources">
            <div class="group-resources-title">Prompts (${prompts.length}):</div>
            ${prompts && prompts.length > 0 ? prompts.map(p => `<span class="badge">${escapeHtml(getProp(p, 'name') || '')}</span>`).join('') : '<span style="color: #95a5a6; font-size: 0.85rem;">No prompts</span>'}
        </div>
        <div class="card-actions">
            <button data-action="manage-group" data-group-id="${id}" data-group-name="${escapeHtml(name)}">Manage Resources</button>
            <button data-action="edit-group" data-group-id="${id}" data-group-name="${escapeHtml(name)}" data-group-description="${escapeHtml(description)}">Edit</button>
            <button class="delete" data-action="delete-group" data-group-id="${id}">Delete</button>
        </div>
    `;
    
    // Attach event listeners
    div.querySelector('[data-action="manage-group"]').addEventListener('click', (e) => {
        manageGroupResources(e.target.dataset.groupId, e.target.dataset.groupName);
    });
    div.querySelector('[data-action="edit-group"]').addEventListener('click', (e) => {
        editGroup(e.target.dataset.groupId, e.target.dataset.groupName, e.target.dataset.groupDescription);
    });
    div.querySelector('[data-action="delete-group"]').addEventListener('click', (e) => {
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
            showMessage(isEditingGroup ? 'Group updated successfully' : 'Group created successfully');
            cancelEditGroup();
            await loadGroups(true);
            await loadAllGroups(true); // Force refresh groups cache
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error saving group', 'error');
        }
    } catch (error) {
        showMessage('Error saving group: ' + error.message, 'error');
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
    if (!confirm('Are you sure you want to delete this group?')) return;
    
    try {
        const response = await fetch(`${API_BASE}/groups/${id}`, {
            method: 'DELETE'
        });
        
        if (response.ok || response.status === 204) {
            showMessage('Group deleted successfully');
            await loadGroups(true);
            await loadAllGroups(true); // Force refresh groups cache
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error deleting group', 'error');
        }
    } catch (error) {
        showMessage('Error deleting group: ' + error.message, 'error');
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
        const currentGroup = groups.find(g => g.id === groupId);
        
        if (!currentGroup) {
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
        
        const groupFileIds = new Set((currentGroup.files || []).map(f => f.id));
        const groupPromptIds = new Set((currentGroup.prompts || []).map(p => p.id));
        
        const modalBody = document.getElementById('modal-content-body');
        modalBody.innerHTML = `
            <h4 style="margin-bottom: 15px;">Group: ${escapeHtml(groupName)}</h4>
            
            <div class="section" style="margin-bottom: 20px;">
                <h5 style="margin-bottom: 10px;">Files</h5>
                <div class="checkbox-group">
                    ${allFiles.map(file => `
                        <div class="checkbox-item">
                            <label>
                                <input type="checkbox" 
                                    value="${file.id}" 
                                    ${groupFileIds.has(file.id) ? 'checked' : ''}
                                    data-action="toggle-file"
                                    data-group-id="${groupId}"
                                    data-file-id="${file.id}">
                                ${escapeHtml(file.label)} (${escapeHtml(file.fileName)})
                            </label>
                        </div>
                    `).join('')}
                    ${allFiles.length === 0 ? '<p style="color: #95a5a6;">No files available</p>' : ''}
                </div>
            </div>
            
            <div class="section">
                <h5 style="margin-bottom: 10px;">Prompts</h5>
                <div class="checkbox-group">
                    ${allPrompts.map(prompt => `
                        <div class="checkbox-item">
                            <label>
                                <input type="checkbox" 
                                    value="${prompt.id}" 
                                    ${groupPromptIds.has(prompt.id) ? 'checked' : ''}
                                    data-action="toggle-prompt"
                                    data-group-id="${groupId}"
                                    data-prompt-id="${prompt.id}">
                                ${escapeHtml(prompt.name)}
                            </label>
                        </div>
                    `).join('')}
                    ${allPrompts.length === 0 ? '<p style="color: #95a5a6;">No prompts available</p>' : ''}
                </div>
            </div>
        `;
        
        // Attach event listeners for checkboxes
        modalBody.querySelectorAll('[data-action="toggle-file"]').forEach(checkbox => {
            checkbox.addEventListener('change', (e) => {
                toggleFileInGroup(e.target.dataset.groupId, e.target.dataset.fileId, e.target.checked);
            });
        });
        modalBody.querySelectorAll('[data-action="toggle-prompt"]').forEach(checkbox => {
            checkbox.addEventListener('change', (e) => {
                togglePromptInGroup(e.target.dataset.groupId, e.target.dataset.promptId, e.target.checked);
            });
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

// Infinite scroll implementation
function setupInfiniteScroll() {
    const containers = [
        { id: 'files-list', loadFn: loadFiles },
        { id: 'prompts-list', loadFn: loadPrompts },
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
        // Load initial data with reset=true to ensure fresh load
        console.log('[INIT] Loading initial data...');
        const results = await Promise.allSettled([
            loadFiles(true),
            loadPrompts(true),
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
            groupsCount: state.groups.data.length
        }));
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
