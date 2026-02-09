// Global toast utilities - define only if not already present
(function () {
    if (window.showMessage) return;

    function createToastContainer() {
        let c = document.getElementById('toast-container');
        if (!c) {
            c = document.createElement('div');
            c.id = 'toast-container';
            c.className = 'toast-container';
            // Append to the document body to ensure it's anchored to the viewport
            document.body.appendChild(c);

            // Apply strong inline styles to override page CSS and lock to bottom-right
            Object.assign(c.style, {
                position: 'fixed',
                right: '20px',
                bottom: '20px',
                zIndex: '99999',
                display: 'flex',
                flexDirection: 'column',
                gap: '10px',
                alignItems: 'flex-end',
                pointerEvents: 'none'
            });
        }
        return c;
    }

    function showMessage(message, type = 'success', timeout = 5000) {
        const container = createToastContainer();
        const toast = document.createElement('div');
        toast.className = `toast ${type}`;
        // enforce compact card font and reset inherited styles
        toast.style.fontFamily = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';
        toast.style.fontSize = '14px';
        toast.style.lineHeight = '1.2';
        toast.style.display = 'flex';
        toast.style.alignItems = 'center';
        toast.style.gap = '12px';
        toast.style.pointerEvents = 'auto';

        // Icon
        const icon = document.createElement('div');
        icon.className = 'toast-icon';
        // make sure icon is sized and centered regardless of page CSS
        Object.assign(icon.style, {
            width: '40px',
            height: '40px',
            borderRadius: '50%',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontWeight: '700',
            fontSize: '18px',
            flex: '0 0 auto'
        });
        if (type === 'success') icon.textContent = '✓';
        else if (type === 'error') icon.textContent = '✖';
        else icon.textContent = 'ℹ';

        // Message
        const msg = document.createElement('div');
        msg.className = 'toast-message';
        msg.textContent = message;
        // reset message styles to avoid large inherited headings
        Object.assign(msg.style, {
            marginRight: '8px',
            color: '#111827',
            fontSize: '14px'
        });

        // Close button
        const closeBtn = document.createElement('button');
        closeBtn.className = 'toast-close';
        closeBtn.setAttribute('aria-label', 'Close notification');
        closeBtn.innerHTML = '\u00d7';
        // inline-reset the button to avoid site-wide button styling
        Object.assign(closeBtn.style, {
            background: 'transparent',
            border: 'none',
            color: '#6b7280',
            fontSize: '16px',
            cursor: 'pointer',
            padding: '6px',
            borderRadius: '6px',
            width: '32px',
            height: '32px',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            flex: '0 0 auto'
        });

        // Progress bar
        const progress = document.createElement('div');
        progress.className = 'toast-progress';
        progress.style.animationDuration = `${timeout}ms`;
        // ensure progress sits at bottom and doesn't inherit other layout
        Object.assign(progress.style, {
            position: 'absolute',
            left: '0',
            bottom: '0',
            height: '4px',
            width: '100%'
        });

        // Compose
        toast.appendChild(icon);
        toast.appendChild(msg);
        toast.appendChild(closeBtn);
        toast.appendChild(progress);
        container.appendChild(toast);

        let h;
        function removeToast() {
            clearTimeout(h);
            progress.style.animationPlayState = 'paused';
            toast.remove();
        }

        h = setTimeout(removeToast, timeout);

        // Pause on hover (pauses timeout and animation)
        toast.addEventListener('mouseenter', () => {
            clearTimeout(h);
            progress.style.animationPlayState = 'paused';
        });
        toast.addEventListener('mouseleave', () => {
            progress.style.animationPlayState = 'running';
            h = setTimeout(removeToast, timeout);
        });

        closeBtn.addEventListener('click', removeToast);
    }

    window.showMessage = showMessage;
})();
