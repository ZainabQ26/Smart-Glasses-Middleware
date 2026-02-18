'use client';

import { useEffect, useState } from 'react';

export default function AdminPage() {
  const [health, setHealth] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // Example API call to backend (proxied via next.config.js)
    fetch('/health')
      .then(res => res.json())
      .then(data => {
        setHealth(data);
        setLoading(false);
      })
      .catch(err => {
        console.error('Failed to fetch health:', err);
        setLoading(false);
      });
  }, []);

  return (
    <main className="min-h-screen p-8">
      <h1 className="text-4xl font-bold mb-4">Admin Dashboard</h1>
      <p className="text-gray-600 mb-8">Manage your Smart Glasses Middleware</p>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="p-6 border rounded-lg shadow">
          <h2 className="text-2xl font-semibold mb-4">System Health</h2>
          {loading ? (
            <p>Loading...</p>
          ) : (
            <pre className="bg-gray-100 p-4 rounded overflow-auto text-sm">
              {JSON.stringify(health, null, 2)}
            </pre>
          )}
        </div>

        <div className="p-6 border rounded-lg shadow">
          <h2 className="text-2xl font-semibold mb-4">Quick Actions</h2>
          <div className="space-y-2">
            <button className="w-full px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">
              Manage Documents
            </button>
            <button className="w-full px-4 py-2 bg-green-600 text-white rounded hover:bg-green-700">
              View Conversations
            </button>
            <button className="w-full px-4 py-2 bg-purple-600 text-white rounded hover:bg-purple-700">
              Configure AI Models
            </button>
          </div>
        </div>
      </div>

      <div className="mt-8">
        <a href="/" className="text-blue-600 hover:underline">← Back to Home</a>
      </div>
    </main>
  );
}
