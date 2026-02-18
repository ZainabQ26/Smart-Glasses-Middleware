'use client';

import { useEffect, useState } from 'react';

export default function MonitoringPage() {
  const [metrics, setMetrics] = useState<any>(null);

  useEffect(() => {
    // Poll health endpoint for monitoring data
    const fetchMetrics = () => {
      fetch('/health')
        .then(res => res.json())
        .then(data => setMetrics(data))
        .catch(err => console.error('Failed to fetch metrics:', err));
    };

    fetchMetrics();
    const interval = setInterval(fetchMetrics, 5000); // Poll every 5s

    return () => clearInterval(interval);
  }, []);

  return (
    <main className="min-h-screen p-8">
      <h1 className="text-4xl font-bold mb-4">Monitoring Dashboard</h1>
      <p className="text-gray-600 mb-8">Real-time system metrics and health</p>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        <div className="p-6 border rounded-lg shadow bg-green-50">
          <h3 className="text-lg font-semibold text-green-800">Postgres</h3>
          <p className={`text-2xl font-bold ${metrics?.checks?.postgres?.healthy ? 'text-green-600' : 'text-red-600'}`}>
            {metrics?.checks?.postgres?.healthy ? '✓ Healthy' : '✗ Down'}
          </p>
        </div>

        <div className="p-6 border rounded-lg shadow bg-blue-50">
          <h3 className="text-lg font-semibold text-blue-800">Redis</h3>
          <p className={`text-2xl font-bold ${metrics?.checks?.redis?.healthy ? 'text-green-600' : 'text-red-600'}`}>
            {metrics?.checks?.redis?.healthy ? '✓ Healthy' : '✗ Down'}
          </p>
        </div>

        <div className="p-6 border rounded-lg shadow bg-purple-50">
          <h3 className="text-lg font-semibold text-purple-800">MinIO</h3>
          <p className={`text-2xl font-bold ${metrics?.checks?.minio?.healthy ? 'text-green-600' : 'text-red-600'}`}>
            {metrics?.checks?.minio?.healthy ? '✓ Healthy' : '✗ Down'}
          </p>
        </div>
      </div>

      <div className="p-6 border rounded-lg shadow">
        <h2 className="text-2xl font-semibold mb-4">Detailed Metrics</h2>
        {metrics ? (
          <pre className="bg-gray-100 p-4 rounded overflow-auto text-sm">
            {JSON.stringify(metrics, null, 2)}
          </pre>
        ) : (
          <p>Loading metrics...</p>
        )}
      </div>

      <div className="mt-8">
        <a href="/" className="text-blue-600 hover:underline">← Back to Home</a>
      </div>
    </main>
  );
}
