/** @type {import('next').NextConfig} */
const nextConfig = {
  // Proxy API and SignalR requests to .NET backend
  async rewrites() {
    const backendUrl = process.env.BACKEND_URL || 'http://localhost:5000';
    
    return [
      {
        source: '/api/:path*',
        destination: `${backendUrl}/api/:path*`,
      },
      {
        source: '/hub/:path*',
        destination: `${backendUrl}/hub/:path*`,
      },
      {
        source: '/health',
        destination: `${backendUrl}/health`,
      },
      {
        source: '/test',
        destination: `${backendUrl}/test`,
      },
    ];
  },
  
  // Enable if you need to proxy static files from .NET wwwroot
  // (e.g., existing admin.html, monitoring.html)
  async redirects() {
    return [
      // Example: redirect old admin path to new Next.js route
      // {
      //   source: '/admin.html',
      //   destination: '/admin',
      //   permanent: true,
      // },
    ];
  },

  // Production optimizations
  reactStrictMode: true,
  swcMinify: true,
  
  // Enable standalone output for optimized Docker images
  output: 'standalone',
};

module.exports = nextConfig;
