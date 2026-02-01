const API_BASE = '/api/admin';

// Tab switching
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
        tab.classList.add('active');
        document.getElementById(tab.dataset.tab).classList.add('active');
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

// Files
async function loadFiles() {
    try {
        const response = await fetch(`${API_BASE}/files`);
        const files = await response.json();
        const list = document.getElementById('files-list');
        
        if (files.length === 0) {
            list.innerHTML = '<div class="empty-state"><p>No files uploaded yet</p></div>';
            return;
        }
        
        list.innerHTML = files.map(file => `
            <div class="card">
                <div class="card-header">
                    <div class="card-title">${escapeHtml(file.label)}</div>
                </div>
                <div class="card-meta">
                    File: ${escapeHtml(file.fileName)} | 
                    Size: ${formatFileSize(file.fileSize)} | 
                    Uploaded: ${new Date(file.uploadedAt).toLocaleString()}
                </div>
                ${file.groups.length > 0 ? `
                    <div style="margin-top: 8px;">
                        ${file.groups.map(g => `<span class="badge">${escapeHtml(g.name)}</span>`).join('')}
                    </div>
                ` : ''}
                <div class="card-actions">
                    <button class="delete" data-file-id="${file.id}" data-action="delete-file">Delete</button>
                </div>
            </div>
        `).join('');
        
        // Attach event listeners
        list.querySelectorAll('[data-action="delete-file"]').forEach(btn => {
            btn.addEventListener('click', () => deleteFile(btn.dataset.fileId));
        });
    } catch (error) {
        showMessage('Error loading files: ' + error.message, 'error');
    }
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
            loadFiles();
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
            loadFiles();
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error deleting file', 'error');
        }
    } catch (error) {
        showMessage('Error deleting file: ' + error.message, 'error');
    }
}

// Prompts
async function loadPrompts() {
    try {
        const response = await fetch(`${API_BASE}/prompts`);
        const prompts = await response.json();
        const list = document.getElementById('prompts-list');
        
        if (prompts.length === 0) {
            list.innerHTML = '<div class="empty-state"><p>No prompts created yet</p></div>';
            return;
        }
        
        list.innerHTML = prompts.map(prompt => `
            <div class="card">
                <div class="card-header">
                    <div class="card-title">${escapeHtml(prompt.name)}</div>
                </div>
                <div class="card-meta">
                    Created: ${new Date(prompt.createdAt).toLocaleString()}
                    ${prompt.updatedAt ? ` | Updated: ${new Date(prompt.updatedAt).toLocaleString()}` : ''}
                </div>
                <div style="margin-top: 10px; padding: 10px; background-color: #f8f9fa; border-radius: 4px; white-space: pre-wrap;">${escapeHtml(prompt.promptText)}</div>
                ${prompt.groups.length > 0 ? `
                    <div style="margin-top: 8px;">
                        ${prompt.groups.map(g => `<span class="badge">${escapeHtml(g.name)}</span>`).join('')}
                    </div>
                ` : ''}
                <div class="card-actions">
                    <button data-action="edit-prompt" data-prompt-id="${prompt.id}" data-prompt-name="${escapeHtml(prompt.name)}" data-prompt-text="${escapeHtml(prompt.promptText)}">Edit</button>
                    <button class="delete" data-action="delete-prompt" data-prompt-id="${prompt.id}">Delete</button>
                </div>
            </div>
        `).join('');
        
        // Attach event listeners
        list.querySelectorAll('[data-action="edit-prompt"]').forEach(btn => {
            btn.addEventListener('click', () => editPrompt(btn.dataset.promptId, btn.dataset.promptName, btn.dataset.promptText));
        });
        list.querySelectorAll('[data-action="delete-prompt"]').forEach(btn => {
            btn.addEventListener('click', () => deletePrompt(btn.dataset.promptId));
        });
    } catch (error) {
        showMessage('Error loading prompts: ' + error.message, 'error');
    }
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
            loadPrompts();
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
            loadPrompts();
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error deleting prompt', 'error');
        }
    } catch (error) {
        showMessage('Error deleting prompt: ' + error.message, 'error');
    }
}

// Groups
async function loadGroups() {
    try {
        const response = await fetch(`${API_BASE}/groups`);
        const groups = await response.json();
        const list = document.getElementById('groups-list');
        
        if (groups.length === 0) {
            list.innerHTML = '<div class="empty-state"><p>No groups created yet</p></div>';
            return;
        }
        
        list.innerHTML = groups.map(group => `
            <div class="card">
                <div class="card-header">
                    <div class="card-title">${escapeHtml(group.name)}</div>
                </div>
                ${group.description ? `<div style="margin-bottom: 10px; color: #555;">${escapeHtml(group.description)}</div>` : ''}
                <div class="card-meta">Created: ${new Date(group.createdAt).toLocaleString()}</div>
                <div class="group-resources">
                    <div class="group-resources-title">Files (${group.files.length}):</div>
                    ${group.files.length > 0 ? group.files.map(f => `<span class="badge">${escapeHtml(f.label)}</span>`).join('') : '<span style="color: #95a5a6; font-size: 0.85rem;">No files</span>'}
                </div>
                <div class="group-resources">
                    <div class="group-resources-title">Prompts (${group.prompts.length}):</div>
                    ${group.prompts.length > 0 ? group.prompts.map(p => `<span class="badge">${escapeHtml(p.name)}</span>`).join('') : '<span style="color: #95a5a6; font-size: 0.85rem;">No prompts</span>'}
                </div>
                <div class="card-actions">
                    <button data-action="manage-group" data-group-id="${group.id}" data-group-name="${escapeHtml(group.name)}">Manage Resources</button>
                    <button data-action="edit-group" data-group-id="${group.id}" data-group-name="${escapeHtml(group.name)}" data-group-description="${escapeHtml(group.description || '')}">Edit</button>
                    <button class="delete" data-action="delete-group" data-group-id="${group.id}">Delete</button>
                </div>
            </div>
        `).join('');
        
        // Attach event listeners
        list.querySelectorAll('[data-action="manage-group"]').forEach(btn => {
            btn.addEventListener('click', () => manageGroupResources(btn.dataset.groupId, btn.dataset.groupName));
        });
        list.querySelectorAll('[data-action="edit-group"]').forEach(btn => {
            btn.addEventListener('click', () => editGroup(btn.dataset.groupId, btn.dataset.groupName, btn.dataset.groupDescription));
        });
        list.querySelectorAll('[data-action="delete-group"]').forEach(btn => {
            btn.addEventListener('click', () => deleteGroup(btn.dataset.groupId));
        });
    } catch (error) {
        showMessage('Error loading groups: ' + error.message, 'error');
    }
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
            loadGroups();
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
            loadGroups();
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error deleting group', 'error');
        }
    } catch (error) {
        showMessage('Error deleting group: ' + error.message, 'error');
    }
}

// Manage Group Resources
async function manageGroupResources(groupId, groupName) {
    try {
        const [filesRes, promptsRes, groupRes] = await Promise.all([
            fetch(`${API_BASE}/files`),
            fetch(`${API_BASE}/prompts`),
            fetch(`${API_BASE}/groups`)
        ]);
        
        const allFiles = await filesRes.json();
        const allPrompts = await promptsRes.json();
        const groups = await groupRes.json();
        const currentGroup = groups.find(g => g.id === groupId);
        
        const groupFileIds = new Set(currentGroup.files.map(f => f.id));
        const groupPromptIds = new Set(currentGroup.prompts.map(p => p.id));
        
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

async function toggleFileInGroup(groupId, fileId, add) {
    try {
        const url = `${API_BASE}/groups/${groupId}/files/${fileId}`;
        const method = add ? 'POST' : 'DELETE';
        
        const response = await fetch(url, { method });
        
        if (response.ok || response.status === 204) {
            showMessage(add ? 'File added to group' : 'File removed from group');
            loadGroups();
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error updating group', 'error');
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
            loadGroups();
        } else {
            const error = await response.json();
            showMessage(error.detail || 'Error updating group', 'error');
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

// Initial load
loadFiles();
loadPrompts();
loadGroups();
