import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';

export default function Home() {
  const [health, setHealth] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch('/health')
      .then(res => res.json())
      .then(data => {
        setHealth(data);
        setLoading(false);
      })
      .catch(err => {
        console.error('Health check failed:', err);
        setLoading(false);
      });
  }, []);

  return (
    <div className="px-4">
      <h1 className="text-3xl font-bold text-gray-900 mb-8">Smart Glasses Middleware</h1>
      
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
        <Link to="/voice" className="block p-6 bg-white rounded-lg shadow hover:shadow-md transition-shadow">
          <h2 className="text-xl font-semibold text-gray-900 mb-2">🎤 Voice Commands</h2>
          <p className="text-gray-600">Speak to interact with AI assistant</p>
        </Link>
        
        <Link to="/documents" className="block p-6 bg-white rounded-lg shadow hover:shadow-md transition-shadow">
          <h2 className="text-xl font-semibold text-gray-900 mb-2">📄 Documents</h2>
          <p className="text-gray-600">View and search your PDFs</p>
        </Link>
      </div>

      <div className="bg-white rounded-lg shadow p-6">
        <h2 className="text-xl font-semibold text-gray-900 mb-4">System Status</h2>
        {loading ? (
          <p className="text-gray-600">Checking backend...</p>
        ) : health ? (
          <div className="space-y-2">
            <StatusBadge label="Postgres" healthy={health.checks?.postgres?.healthy} />
            <StatusBadge label="Redis" healthy={health.checks?.redis?.healthy} />
            <StatusBadge label="MinIO" healthy={health.checks?.minio?.healthy} />
          </div>
        ) : (
          <p className="text-red-600">❌ Backend offline</p>
        )}
      </div>
    </div>
  );
}

function StatusBadge({ label, healthy }: { label: string; healthy?: boolean }) {
  return (
    <div className="flex items-center space-x-2">
      <span className={`inline-block w-3 h-3 rounded-full ${healthy ? 'bg-green-500' : 'bg-red-500'}`} />
      <span className="text-gray-900 font-medium">{label}</span>
      <span className="text-gray-600">{healthy ? '✓ Healthy' : '✗ Down'}</span>
    </div>
  );
}
