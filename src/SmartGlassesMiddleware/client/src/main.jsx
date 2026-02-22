import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.jsx'
import WakeupGate from './WakeupGate.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <WakeupGate>
      <App />
    </WakeupGate>
  </StrictMode>,
)
