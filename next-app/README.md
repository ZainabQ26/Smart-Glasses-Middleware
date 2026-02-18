# Smart Glasses Middleware - Next.js Frontend

Next.js frontend with server-side rendering that proxies API requests to the .NET backend.

## Architecture

- **Next.js** (port 3000) - Serves UI at root path with SSR
- **.NET Backend** (port 5000) - API endpoints, SignalR hubs, health checks
- **Proxy Configuration** - `next.config.js` rewrites `/api/*` and `/hub/*` to backend

## Quick Start

### Development

1. Install dependencies:
```bash
cd next-app
npm install
```

2. Start .NET backend (in separate terminal):
```bash
cd ../src/SmartGlassesMiddleware
dotnet run
```

3. Start Next.js dev server:
```bash
npm run dev
```

4. Open http://localhost:3000

### Production Build

```bash
npm run build
npm start
```

## Environment Variables

Create `.env.local` for local development:

```env
BACKEND_URL=http://localhost:5000
```

For production/Docker:
```env
BACKEND_URL=http://smartglasses-api:5000
```

## Docker

Build and run:
```bash
docker build -t smartglasses-next .
docker run -p 3000:3000 -e BACKEND_URL=http://backend:5000 smartglasses-next
```

## API Integration

API calls are automatically proxied:

```typescript
// Client-side or server-side fetch
const response = await fetch('/api/chat/ask', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ prompt: 'Hello' }),
});
```

The request is transparently routed to `http://localhost:5000/api/chat/ask`.

## SignalR Integration

For SignalR (WebSocket) connections:

```typescript
import * as signalR from '@microsoft/signalr';

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hub/notifications') // proxied to backend
  .build();

await connection.start();
```

## Migrating Existing Pages

The current .NET app serves static HTML from `wwwroot/`:
- `admin.html`
- `monitoring.html`

Options:
1. **Convert to Next.js pages** (recommended):
   - Create `src/app/admin/page.tsx`
   - Create `src/app/monitoring/page.tsx`
   - Port the JS logic to React components

2. **Keep as static and redirect** (quick migration):
   - Copy files to `next-app/public/`
   - Access via `/admin.html` (served as static)
   - Or add redirects in `next.config.js`

## Deployment

See `docker-compose.yml` in root for local multi-service setup.

For AWS ECS/Fargate, deploy as separate services with ALB routing.
