import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.jsx'

const rootElement = document.getElementById('root');
const adminChatElement = document.getElementById('admin-chat-root');

if (adminChatElement) {
  createRoot(adminChatElement).render(<App mode="admin" />);
} else if (rootElement) {
  createRoot(rootElement).render(<App mode="voice" />);
}
