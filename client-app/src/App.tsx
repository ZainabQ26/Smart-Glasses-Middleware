import { Routes, Route, Link } from 'react-router-dom';
import Home from './pages/Home';
import VoiceCommand from './pages/VoiceCommand';
import DocumentViewer from './pages/DocumentViewer';

function App() {
  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-white shadow-sm">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between h-16">
            <div className="flex space-x-8">
              <Link to="/" className="inline-flex items-center px-1 pt-1 text-sm font-medium text-gray-900">
                Home
              </Link>
              <Link to="/voice" className="inline-flex items-center px-1 pt-1 text-sm font-medium text-gray-500 hover:text-gray-900">
                Voice
              </Link>
              <Link to="/documents" className="inline-flex items-center px-1 pt-1 text-sm font-medium text-gray-500 hover:text-gray-900">
                Documents
              </Link>
            </div>
          </div>
        </div>
      </nav>

      <main className="max-w-7xl mx-auto py-6 sm:px-6 lg:px-8">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/voice" element={<VoiceCommand />} />
          <Route path="/documents" element={<DocumentViewer />} />
        </Routes>
      </main>
    </div>
  );
}

export default App;
