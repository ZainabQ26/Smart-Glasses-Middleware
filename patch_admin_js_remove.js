<<<<<<< SEARCH
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

            const moveToHighlightMatch = (findPrevious = false) => {
                try {
                    const pdfApp = window.PDFViewerApplication;
                    const findController = pdfApp?.findController;
                    const existingState = findController?._state || {};
                    const existingQuery = typeof existingState.query === 'string'
                        ? existingState.query.trim()
                        : '';

                    if (findController && typeof findController.executeCommand === 'function' && existingQuery) {
                        findController.executeCommand('findagain', {
                            query: existingQuery,
                            phraseSearch: existingState.phraseSearch !== false,
                            caseSensitive: !!existingState.caseSensitive,
                            entireWord: !!existingState.entireWord,
                            highlightAll: true,
                            findPrevious: !!findPrevious
                        });
                        return true;
                    }

                    // Fallback to toolbar buttons when available.
                    const buttonId = findPrevious ? 'findPrevious' : 'findNext';
                    const findButton = document.getElementById(buttonId);
                    if (findButton && typeof findButton.click === 'function') {
                        findButton.click();
                        return true;
                    }
                } catch (err) {
                    console.warn('[PDF HIGHLIGHT NAV] Failed to move between highlighted matches', err);
                }

                return false;
            };

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

            if (/\b(?:next highlight|next highlighted|next highlighted word|next highlighted phrase|go to next highlight|find next highlight)\b/.test(lowered)) {
                const moved = moveToHighlightMatch(false);
                responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;">${moved ? 'Next highlighted match' : 'No active highlighted match to navigate. Highlight a term first.'}</div>`;
                document.getElementById('prompt-name').value = '';
                return;
            }

            if (/\b(?:previous highlight|prev highlight|back highlight|previous highlighted|previous highlighted word|go to previous highlight|find previous highlight)\b/.test(lowered)) {
                const moved = moveToHighlightMatch(true);
                responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;">${moved ? 'Previous highlighted match' : 'No active highlighted match to navigate. Highlight a term first.'}</div>`;
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

                if (normalizedAnswer.includes('__ACTION:NEXT_HIGHLIGHT__')) {
                    const moved = moveToHighlightMatch(false);
                    responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;">${moved ? 'Next highlighted match' : 'No active highlighted match to navigate. Highlight a term first.'}</div>`;
                    return;
                }

                if (normalizedAnswer.includes('__ACTION:PREVIOUS_HIGHLIGHT__')) {
                    const moved = moveToHighlightMatch(true);
                    responseContent.innerHTML = `<div style="padding: 15px; white-space: pre-wrap;">${moved ? 'Previous highlighted match' : 'No active highlighted match to navigate. Highlight a term first.'}</div>`;
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
=======
>>>>>>> REPLACE
