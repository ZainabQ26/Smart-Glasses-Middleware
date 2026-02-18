export default function Home() {
  return (
    <main className="min-h-screen p-8">
      <h1 className="text-4xl font-bold mb-4">Smart Glasses Middleware</h1>
      <p className="text-lg mb-8">
        Next.js frontend with server-side rendering
      </p>
      
      <div className="space-y-4">
        <div className="p-4 border rounded">
          <h2 className="text-2xl font-semibold mb-2">Admin</h2>
          <a href="/admin" className="text-blue-600 hover:underline">
            Go to Admin Dashboard
          </a>
        </div>
        
        <div className="p-4 border rounded">
          <h2 className="text-2xl font-semibold mb-2">Monitoring</h2>
          <a href="/monitoring" className="text-blue-600 hover:underline">
            Go to Monitoring Dashboard
          </a>
        </div>
        
        <div className="p-4 border rounded">
          <h2 className="text-2xl font-semibold mb-2">Health Check</h2>
          <a href="/health" className="text-blue-600 hover:underline">
            Check API Health
          </a>
        </div>
      </div>
    </main>
  );
}
