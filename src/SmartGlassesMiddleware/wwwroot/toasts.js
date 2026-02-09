// Global toast utilities - define only if not already present
(function () {
    if (window.showMessage) return;

    function createToastContainer() {
        let host = document.getElementById('toast-container');
        if (!host) {
            host = document.createElement('div');
            host.id = 'toast-container';
            // Append host to body so it's in viewport coordinate space
            document.body.appendChild(host);

            // Position the host element (host itself has no inner styles)
            Object.assign(host.style, {
                position: 'fixed',
                right: '20px',
                bottom: '20px',
                zIndex: '999999',
                display: 'block',
                pointerEvents: 'none'
            });

            // Create a shadow root to isolate toast styles from page CSS
            const shadow = host.attachShadow({ mode: 'open' });

            // Minimal isolated CSS for toasts (keeps layout stable regardless of page)
            const css = `
                :host { all: initial; }
                .container { display: flex; flex-direction: column; gap: 12px; align-items: flex-end; }
                .toast { box-sizing: border-box; width: 360px; padding: 14px 16px; border-radius: 12px; background: #fff; color: #111827; border: 1px solid rgba(16,24,32,0.06); box-shadow: 0 8px 30px rgba(16,24,32,0.12); display:flex; align-items:center; gap:12px; position:relative; overflow:hidden; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif; }
                .toast .icon { width:40px; height:40px; border-radius:50%; display:inline-flex; align-items:center; justify-content:center; font-weight:700; font-size:18px; flex:0 0 auto; color:#fff }
                .toast .msg { flex:1 1 auto; font-size:14px; margin-right:8px; }
                .toast .close { background:transparent; border:none; color:#6b7280; font-size:16px; cursor:pointer; padding:6px; border-radius:6px; width:32px; height:32px; display:inline-flex; align-items:center; justify-content:center; flex:0 0 auto }
                .toast.success { border-left:6px solid #16a34a }
                .toast.error { border-left:6px solid #dc2626 }
                .toast.info { border-left:6px solid #3b82f6 }
                .toast.success .icon { background:#16a34a }
                .toast.error .icon { background:#dc2626 }
                .toast.info .icon { background:#2563eb }
                .progress { position:absolute; left:0; bottom:0; height:4px; width:100%; transform-origin:left center; animation-name:progressAnim; animation-timing-function:linear; }
                @keyframes progressAnim { from { transform: scaleX(1); } to { transform: scaleX(0); } }
            `;

            const styleEl = document.createElement('style');
            styleEl.textContent = css;

            const container = document.createElement('div');
            container.className = 'container';

            shadow.appendChild(styleEl);
            shadow.appendChild(container);
        }
        return host;
    }

    function showMessage(message, type = 'success', timeout = 5000) {
        const host = createToastContainer();
        const shadow = host.shadowRoot;
        const container = shadow.querySelector('.container');
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
        icon.className = 'icon';
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
        msg.className = 'msg';
        msg.textContent = message;
        // reset message styles to avoid large inherited headings
        Object.assign(msg.style, {
            marginRight: '8px',
            color: '#111827',
            fontSize: '14px'
        });

        // Close button
        const closeBtn = document.createElement('button');
        closeBtn.className = 'close';
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
        progress.className = 'progress';
        progress.style.animationDuration = `${timeout}ms`;
        // ensure progress sits at bottom and doesn't inherit other layout
        Object.assign(progress.style, {
            position: 'absolute',
            left: '0',
            bottom: '0',
            height: '4px',
            width: '100%'
        });

        // Color the progress bar per toast type
        if (type === 'success') {
            progress.style.background = 'linear-gradient(90deg,#10b981,#16a34a)';
        } else if (type === 'error') {
            progress.style.background = 'linear-gradient(90deg,#ef4444,#dc2626)';
        } else if (type === 'info') {
            progress.style.background = 'linear-gradient(90deg,#3b82f6,#06b6d4)';
        } else {
            progress.style.background = 'linear-gradient(90deg,#4b5563,#9ca3af)';
        }

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
            // remove host if no toasts left to avoid stale element
            const host = document.getElementById('toast-container');
            if (host && host.shadowRoot) {
                const left = host.shadowRoot.querySelectorAll('.toast').length;
                if (left === 0) host.remove();
            }
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
