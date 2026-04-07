import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.jsx'

const rootElement = document.getElementById('root');
const adminChatElement = document.getElementById('admin-chat-root');

if (adminChatElement) {
  // Resolve the current admin language (data attribute takes precedence over localStorage).
  const getAdminLang = () => {
    const el = document.getElementById('admin-chat-root');
    const fromAttr = el?.dataset?.language;
    if (fromAttr === 'es' || fromAttr === 'en') return fromAttr;
    const fromStorage = localStorage.getItem('adminLanguage');
    return fromStorage === 'es' ? 'es' : 'en';
  };

  const adminRoot = createRoot(adminChatElement);
  const renderAdmin = () => adminRoot.render(<App mode="admin" language={getAdminLang()} />);

  renderAdmin();

  // Re-render when the admin panel language toggle fires.
  window.addEventListener('adminlanguagechange', renderAdmin);
} else if (rootElement) {
  // Detect language from data attribute first, then html lang, then URL path.
  const fromAttr = rootElement.dataset?.language;
  const fromHtml = document.documentElement.lang;
  const fromPath = window.location.pathname.startsWith('/es') ? 'es' : null;
  const lang = (fromAttr === 'es' || fromAttr === 'en')
    ? fromAttr
    : fromHtml?.startsWith('es')
      ? 'es'
      : fromPath ?? 'en';

  createRoot(rootElement).render(<App mode="voice" language={lang} />);
}
