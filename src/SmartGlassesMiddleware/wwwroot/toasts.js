// Global toast utilities - define only if not already present
(function () {
    if (window.showMessage) return;

    function createToastContainer() {
        let c = document.getElementById('toast-container');
        if (!c) {
            c = document.createElement('div');
            c.id = 'toast-container';
            c.className = 'toast-container';
            // Append to the document root to avoid being influenced by transformed ancestors
            const root = document.documentElement || document.body;
            root.appendChild(c);

            // Ensure fixed positioning via inline styles so it's anchored to the viewport
            c.style.position = 'fixed';
            c.style.right = '20px';
            c.style.bottom = '20px';
            c.style.zIndex = '99999';
            c.style.display = 'flex';
            c.style.flexDirection = 'column';
            c.style.gap = '10px';
        }
        return c;
    }

    function showMessage(message, type = 'success', timeout = 5000) {
        const container = createToastContainer();
        const toast = document.createElement('div');
        toast.className = `toast ${type}`;

        const content = document.createElement('div');
        content.className = 'toast-content';
        content.textContent = message;

        const closeBtn = document.createElement('button');
        closeBtn.className = 'toast-close';
        closeBtn.setAttribute('aria-label', 'Close notification');
        closeBtn.innerHTML = '\u00d7';
        closeBtn.addEventListener('click', () => {
            clearTimeout(h);
            toast.remove();
        });

        toast.appendChild(content);
        toast.appendChild(closeBtn);
        container.appendChild(toast);

        let h = setTimeout(() => toast.remove(), timeout);

        toast.addEventListener('mouseenter', () => clearTimeout(h));
        toast.addEventListener('mouseleave', () => {
            h = setTimeout(() => toast.remove(), timeout);
        });
    }

    window.showMessage = showMessage;
})();
