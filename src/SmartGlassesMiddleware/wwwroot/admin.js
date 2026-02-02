const API_BASE = '/api/admin';

// Pagination state
const state = {
    files: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    prompts: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    groups: { page: 1, pageSize: 20, hasMore: true, loading: false, data: [] },
    allGroups: [] // Cache of all groups for quick-add
};

// Dark mode toggle
function initTheme() {
    const savedTheme = localStorage.getItem('theme') || 'light';
    document.documentElement.setAttribute('data-theme', savedTheme);
    updateThemeButton(savedTheme);
}

function updateThemeButton(theme) {
    const button = document.getElementById('theme-toggle');
    if (theme === 'dark') {
        button.textContent = '☀️ Light Mode';
    } else {
        button.textContent = '🌙 Dark Mode';
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const themeToggle = document.getElementById('theme-toggle');
    themeToggle.addEventListener('click', () => {
        const currentTheme = document.documentElement.getAttribute('data-theme');
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', newTheme);
        localStorage.setItem('theme', newTheme);
        updateThemeButton(newTheme);
    });
});

// Tab switching
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
        tab.classList.add('active');
        document.getElementById(tab.dataset.tab).classList.add('active');
    });
});

// Show message with better error handling
function showMessage(message, type = 'success') {
    const container = document.getElementById('message-container');
    const div = document.createElement('div');
    div.className = `message ${type}`;
    
    // Handle both string and object messages
    if (typeof message === 'object') {
        if (message.errors) {
            // Validation errors from API
            const errorMessages = Object.entries(message.errors)
                .map(([field, msgs]) => `${field}: ${msgs.join(', ')}`)
                .join('; ');
            div.textContent = errorMessages;
        } else if (message.detail) {
            div.textContent = message.detail;
        } else if (message.title) {
            div.textContent = message.title;
        } else {
            div.textContent = JSON.stringify(message);
        }
    } else {
        div.textContent = message;
    }
    
    container.appendChild(div);
    setTimeout(() => div.remove(), 5000);
}

// Client-side validation
function validateField(input, errorElementId, validationFn, errorMessage) {
    const errorElement = document.getElementById(errorElementId);
    const isValid = validationFn(input.value);
    
    if (!isValid) {
        input.classList.add('invalid');
        errorElement.textContent = errorMessage;
        errorElement.classList.add('visible');
    } else {
        input.classList.remove('invalid');
        errorElement.classList.remove('visible');
    }
    
    return isValid;
}

function clearFieldError(input, errorElementId) {
    input.classList.remove('invalid');
    const errorElement = document.getElementById(errorElementId);
    if (errorElement) {
        errorElement.classList.remove('visible');
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
        const response = await fetch(`${API_BASE}/files?page=${state.files.page}&pageSize=${state.files.pageSize}`);
        const result = await response.json();
        const list = document.getElementById('files-list');
        
        hideLoading('files-list');
        
        if (result.data && result.data.length > 0) {
            state.files.data.push(...result.data);
            
            const fragment = document.createDocumentFragment();
            result.data.forEach(file => {
                const div = createFileCard(file);
                fragment.appendChild(div);
            });
            list.appendChild(fragment);
            
            state.files.hasMore = result.pagination.hasNext;
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
    div.className = 'card clickable';
    div.innerHTML = `
        <div class="card-header">
            <div class="card-title">${escapeHtml(file.label)}</div>
        </div>
        <div class="card-meta">
            File: ${escapeHtml(file.fileName)} | 
            Size: ${formatFileSize(file.fileSize)} | 
            Uploaded: ${new Date(file.uploadedAt).toLocaleString()}
        </div>
        ${file.groups && file.groups.length > 0 ? `
            <div style="margin-top: 8px;">
                ${file.groups.map(g => `<span class="badge">${escapeHtml(g.name)}</span>`).join('')}
            </div>
        ` : ''}
        <div class="card-actions">
            <button data-action="preview-file" data-file-id="${file.id}" data-file-name="${escapeHtml(file.fileName)}" data-file-label="${escapeHtml(file.label)}">Preview</button>
            <button data-action="download-file" data-file-id="${file.id}" data-file-name="${escapeHtml(file.fileName)}">Download</button>
            <button data-action="add-to-group-file" data-file-id="${file.id}" data-file-label="${escapeHtml(file.label)}">Add to Group</button>
            <button class="delete" data-file-id="${file.id}" data-action="delete-file">Delete</button>
        </div>
    `;
    
    // Attach event listeners
    div.querySelector('[data-action="preview-file"]').addEventListener('click', (e) => {
        e.stopPropagation();
        previewFile(e.target.dataset.fileId, e.target.dataset.fileName, e.target.dataset.fileLabel);
    });
    div.querySelector('[data-action="download-file"]').addEventListener('click', (e) => {
        e.stopPropagation();
        downloadFile(e.target.dataset.fileId, e.target.dataset.fileName);
    });
    div.querySelector('[data-action="delete-file"]').addEventListener('click', (e) => {
        e.stopPropagation();
        deleteFile(e.target.dataset.fileId);
    });
    div.querySelector('[data-action="add-to-group-file"]').addEventListener('click', (e) => {
        e.stopPropagation();
        showQuickAddToGroup(e.target.dataset.fileId, 'file', e.target.dataset.fileLabel);
    });
    
    // Click on card (not button) to preview
    div.addEventListener('click', (e) => {
        if (!e.target.closest('button')) {
            const previewBtn = div.querySelector('[data-action="preview-file"]');
            previewFile(previewBtn.dataset.fileId, previewBtn.dataset.fileName, previewBtn.dataset.fileLabel);
        }
    });
    
    return div;
}

// File preview functionality
async function previewFile(fileId, fileName, fileLabel) {
    try {
        const modal = document.getElementById('preview-modal');
        const title = document.getElementById('preview-title');
        const body = document.getElementById('preview-body');
        
        title.textContent = fileLabel || fileName;
        body.innerHTML = '<div class="loading-indicator">Loading preview...</div>';
        modal.classList.add('active');
        
        const response = await fetch(`${API_BASE}/files/${fileId}/download`);
        if (!response.ok) throw new Error('Failed to load file');
        
        const blob = await response.blob();
        const fileType = fileName.split('.').pop().toLowerCase();
        const mimeType = blob.type;
        
        let previewHTML = '';
        
        // Image preview
        if (mimeType.startsWith('image/')) {
            const url = URL.createObjectURL(blob);
            previewHTML = `
                <div class="preview-container">
                    <img src="${url}" alt="${escapeHtml(fileName)}" />
                </div>
                <button id="preview-download-btn" data-file-id="${fileId}" data-file-name="${escapeHtml(fileName)}">Download File</button>
            `;
        }
        // PDF preview
        else if (mimeType === 'application/pdf' || fileType === 'pdf') {
            const url = URL.createObjectURL(blob);
            previewHTML = `
                <div class="preview-container">
                    <iframe src="${url}" type="application/pdf"></iframe>
                </div>
                <button id="preview-download-btn" data-file-id="${fileId}" data-file-name="${escapeHtml(fileName)}">Download File</button>
            `;
        }
        // Text files
        else if (mimeType.startsWith('text/') || ['txt', 'json', 'xml', 'csv', 'log', 'md'].includes(fileType)) {
            const text = await blob.text();
            previewHTML = `
                <div class="preview-container">
                    <pre>${escapeHtml(text)}</pre>
                </div>
                <button id="preview-download-btn" data-file-id="${fileId}" data-file-name="${escapeHtml(fileName)}">Download File</button>
            `;
        }
        // No preview available
        else {
            previewHTML = `
                <div class="preview-container">
                    <p style="text-align: center; color: var(--text-tertiary);">Preview not available for this file type</p>
                    <p style="text-align: center; margin-top: 10px;">File: ${escapeHtml(fileName)}</p>
                </div>
                <button id="preview-download-btn" data-file-id="${fileId}" data-file-name="${escapeHtml(fileName)}">Download File</button>
            `;
        }
        
        body.innerHTML = previewHTML;
        
        // Attach event listener to download button
        const downloadBtn = body.querySelector('#preview-download-btn');
        if (downloadBtn) {
            downloadBtn.addEventListener('click', () => {
                downloadFile(downloadBtn.dataset.fileId, downloadBtn.dataset.fileName);
            });
        }
    } catch (error) {
        showMessage('Error loading preview: ' + error.message, 'error');
        closePreviewModal();
    }
}

// Download file
async function downloadFile(fileId, fileName) {
    try {
        const response = await fetch(`${API_BASE}/files/${fileId}/download`);
        if (!response.ok) throw new Error('Download failed');
        
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        showMessage('File downloaded successfully');
    } catch (error) {
        showMessage('Error downloading file: ' + error.message, 'error');
    }
}

function closePreviewModal() {
    document.getElementById('preview-modal').classList.remove('active');
}

document.getElementById('upload-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fileInput = document.getElementById('file-input');
    const labelInput = document.getElementById('file-label');
    const label = labelInput.value.trim();
    
    // Client-side validation
    if (!fileInput.files || fileInput.files.length === 0) {
        showMessage('Please select a file', 'error');
        return;
    }
    
    if (!label) {
        showMessage('Please enter a label', 'error');
        labelInput.classList.add('invalid');
        return;
    }
    
    labelInput.classList.remove('invalid');
    
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
            loadFiles(true);
        } else {
            const error = await response.json();
            showMessage(error, 'error');
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
            loadFiles(true);
        } else {
            const error = await response.json();
            showMessage(error, 'error');
        }
    } catch (error) {
        showMessage('Error deleting file: ' + error.message, 'error');
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
        document.getElementById('prompts-list').innerHTML = '';
    }
    
    state.prompts.loading = true;
    showLoading('prompts-list');
    
    try {
        const response = await fetch(`${API_BASE}/prompts?page=${state.prompts.page}&pageSize=${state.prompts.pageSize}`);
        const result = await response.json();
        const list = document.getElementById('prompts-list');
        
        hideLoading('prompts-list');
        
        if (result.data && result.data.length > 0) {
            state.prompts.data.push(...result.data);
            
            const fragment = document.createDocumentFragment();
            result.data.forEach(prompt => {
                const div = createPromptCard(prompt);
                fragment.appendChild(div);
            });
            list.appendChild(fragment);
            
            state.prompts.hasMore = result.pagination.hasNext;
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
    const displayName = prompt.name || 'Unnamed Prompt';
    div.innerHTML = `
        <div class="card-header">
            <div class="card-title">${escapeHtml(displayName)}</div>
        </div>
        <div class="card-meta">
            Created: ${new Date(prompt.createdAt).toLocaleString()}
            ${prompt.updatedAt ? ` | Updated: ${new Date(prompt.updatedAt).toLocaleString()}` : ''}
        </div>
        <div style="margin-top: 10px; padding: 10px; background-color: var(--bg-tertiary); border-radius: 4px; white-space: pre-wrap;">${escapeHtml(prompt.promptText)}</div>
        ${prompt.groups && prompt.groups.length > 0 ? `
            <div style="margin-top: 8px;">
                ${prompt.groups.map(g => `<span class="badge">${escapeHtml(g.name)}</span>`).join('')}
            </div>
        ` : ''}
        <div class="card-actions">
            <button class="execute" data-action="execute-prompt" data-prompt-id="${prompt.id}">Execute</button>
            <button data-action="add-to-group-prompt" data-prompt-id="${prompt.id}" data-prompt-name="${escapeHtml(displayName)}">Add to Group</button>
            <button data-action="edit-prompt" data-prompt-id="${prompt.id}" data-prompt-name="${escapeHtml(prompt.name || '')}" data-prompt-text="${escapeHtml(prompt.promptText)}">Edit</button>
            <button class="delete" data-action="delete-prompt" data-prompt-id="${prompt.id}">Delete</button>
        </div>
        <div id="execution-result-${prompt.id}" class="execution-result" style="display: none;"></div>
    `;
    
    // Attach event listeners
    div.querySelector('[data-action="execute-prompt"]').addEventListener('click', (e) => {
        executePrompt(e.target.dataset.promptId);
    });
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

// Execute prompt
async function executePrompt(promptId) {
    try {
        const resultDiv = document.getElementById(`execution-result-${promptId}`);
        resultDiv.style.display = 'block';
        resultDiv.innerHTML = '<div class="loading-indicator">Executing prompt...</div>';
        
        const response = await fetch(`${API_BASE}/prompts/${promptId}/execute`, {
            method: 'POST'
        });
        
        if (response.ok) {
            const result = await response.json();
            resultDiv.innerHTML = `
                <strong>Execution Result:</strong>
                <pre>${escapeHtml(JSON.stringify(result, null, 2))}</pre>
            `;
            showMessage('Prompt executed successfully');
        } else {
            const error = await response.json();
            resultDiv.innerHTML = `
                <strong style="color: var(--danger-color);">Execution Failed:</strong>
                <pre>${escapeHtml(JSON.stringify(error, null, 2))}</pre>
            `;
            showMessage('Failed to execute prompt', 'error');
        }
    } catch (error) {
        const resultDiv = document.getElementById(`execution-result-${promptId}`);
        resultDiv.innerHTML = `
            <strong style="color: var(--danger-color);">Error:</strong>
            <pre>${escapeHtml(error.message)}</pre>
        `;
        showMessage('Error executing prompt: ' + error.message, 'error');
    }
}

let isEditingPrompt = false;

document.getElementById('prompt-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const id = document.getElementById('prompt-id').value;
    const name = document.getElementById('prompt-name').value.trim();
    const text = document.getElementById('prompt-text').value.trim();
    const fileInput = document.getElementById('prompt-file');
    
    // Client-side validation
    let isValid = true;
    
    if (!text) {
        validateField(
            document.getElementById('prompt-text'),
            'prompt-text-error',
            (val) => val.trim().length > 0,
            'Prompt text is required'
        );
        isValid = false;
    } else {
        clearFieldError(document.getElementById('prompt-text'), 'prompt-text-error');
    }
    
    if (!isValid) {
        showMessage('Please fix validation errors', 'error');
        return;
    }
    
    try {
        const formData = new FormData();
        if (name) formData.append('name', name);
        formData.append('promptText', text);
        if (fileInput.files && fileInput.files.length > 0) {
            formData.append('file', fileInput.files[0]);
        }
        
        const url = isEditingPrompt ? `${API_BASE}/prompts/${id}` : `${API_BASE}/prompts`;
        const method = isEditingPrompt ? 'PUT' : 'POST';
        
        const response = await fetch(url, {
            method,
            body: formData
        });
        
        if (response.ok) {
            showMessage(isEditingPrompt ? 'Prompt updated successfully' : 'Prompt created successfully');
            cancelEditPrompt();
            loadPrompts(true);
        } else {
            const error = await response.json();
            showMessage(error, 'error');
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
    clearFieldError(document.getElementById('prompt-text'), 'prompt-text-error');
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
            loadPrompts(true);
        } else {
            const error = await response.json();
            showMessage(error, 'error');
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
        const response = await fetch(`${API_BASE}/groups?page=${state.groups.page}&pageSize=${state.groups.pageSize}`);
        const result = await response.json();
        const list = document.getElementById('groups-list');
        
        hideLoading('groups-list');
        
        if (result.data && result.data.length > 0) {
            state.groups.data.push(...result.data);
            
            const fragment = document.createDocumentFragment();
            result.data.forEach(group => {
                const div = createGroupCard(group);
                fragment.appendChild(div);
            });
            list.appendChild(fragment);
            
            state.groups.hasMore = result.pagination.hasNext;
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
    div.innerHTML = `
        <div class="card-header">
            <div class="card-title">${escapeHtml(group.name)}</div>
        </div>
        ${group.description ? `<div style="margin-bottom: 10px; color: var(--text-secondary);">${escapeHtml(group.description)}</div>` : ''}
        <div class="card-meta">Created: ${new Date(group.createdAt).toLocaleString()}</div>
        <div class="group-resources">
            <div class="group-resources-title">Files (${group.files?.length || 0}):</div>
            ${group.files && group.files.length > 0 ? group.files.map(f => `<span class="badge">${escapeHtml(f.label)}</span>`).join('') : '<span style="color: var(--text-tertiary); font-size: 0.85rem;">No files</span>'}
        </div>
        <div class="group-resources">
            <div class="group-resources-title">Prompts (${group.prompts?.length || 0}):</div>
            ${group.prompts && group.prompts.length > 0 ? group.prompts.map(p => `<span class="badge">${escapeHtml(p.name || 'Unnamed Prompt')}</span>`).join('') : '<span style="color: var(--text-tertiary); font-size: 0.85rem;">No prompts</span>'}
        </div>
        <div class="card-actions">
            <button data-action="manage-group" data-group-id="${group.id}" data-group-name="${escapeHtml(group.name)}">Manage Resources</button>
            <button data-action="edit-group" data-group-id="${group.id}" data-group-name="${escapeHtml(group.name)}" data-group-description="${escapeHtml(group.description || '')}">Edit</button>
            <button class="delete" data-action="delete-group" data-group-id="${group.id}">Delete</button>
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
            loadGroups(true);
            loadAllGroups(); // Refresh groups cache
        } else {
            const error = await response.json();
            showMessage(error, 'error');
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
            loadGroups(true);
            loadAllGroups(); // Refresh groups cache
        } else {
            const error = await response.json();
            showMessage(error, 'error');
        }
    } catch (error) {
        showMessage('Error deleting group: ' + error.message, 'error');
    }
}

// Manage Group Resources (existing modal functionality)
async function manageGroupResources(groupId, groupName) {
    try {
        const [filesRes, promptsRes, groupRes] = await Promise.all([
            fetch(`${API_BASE}/files?page=1&pageSize=100`),
            fetch(`${API_BASE}/prompts?page=1&pageSize=100`),
            fetch(`${API_BASE}/groups/${groupId}`)
        ]);
        
        if (!filesRes.ok || !promptsRes.ok || !groupRes.ok) {
            throw new Error('Failed to load resources');
        }
        
        const filesResult = await filesRes.json();
        const promptsResult = await promptsRes.json();
        const currentGroup = await groupRes.json();
        
        const allFiles = filesResult.data || [];
        const allPrompts = promptsResult.data || [];
        
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
                    ${allFiles.length === 0 ? '<p style="color: var(--text-tertiary);">No files available</p>' : ''}
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
                                ${escapeHtml(prompt.name || 'Unnamed Prompt')}
                            </label>
                        </div>
                    `).join('')}
                    ${allPrompts.length === 0 ? '<p style="color: var(--text-tertiary);">No prompts available</p>' : ''}
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
async function loadAllGroups() {
    try {
        const response = await fetch(`${API_BASE}/groups?page=1&pageSize=100`);
        const result = await response.json();
        state.allGroups = result.data || [];
    } catch (error) {
        console.error('Error loading all groups:', error);
    }
}

async function showQuickAddToGroup(resourceId, resourceType, resourceName) {
    if (state.allGroups.length === 0) {
        await loadAllGroups();
    }
    
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
        } else {
            const error = await response.json();
            showMessage(error, 'error');
        }
    } catch (error) {
        showMessage('Error updating group: ' + error.message, 'error');
    }
}

async function togglePromptInGroup(groupId, promptId, add) {
    try {
        const url = `${API_BASE}/groups/${groupId}/prompts/${promptId}`;
        const method = add ? 'POST' : 'DELETE';
        
        const response = await fetch(url, { method });
        
        if (response.ok || response.status === 204) {
            showMessage(add ? 'Prompt added to group' : 'Prompt removed from group');
        } else {
            const error = await response.json();
            showMessage(error, 'error');
        }
    } catch (error) {
        showMessage('Error updating group: ' + error.message, 'error');
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
document.getElementById('close-preview-btn').addEventListener('click', closePreviewModal);

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

// Initial load
async function init() {
    initTheme();
    await Promise.all([
        loadFiles(),
        loadPrompts(),
        loadGroups(),
        loadAllGroups()
    ]);
    setupInfiniteScroll();
}

init();
