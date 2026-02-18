# Smart Glasses Client (Vite + React)

Lightweight SPA optimized for smart glasses with voice commands and PDF viewing.

## Features

- 🎤 **Voice Commands** - Record audio → transcribe → AI response
- 📄 **PDF Viewer** - Browse and view documents from backend
- ⚡ **Fast & Lightweight** - ~80KB base bundle (vs Next.js ~200KB)
- 🔄 **Server-dependent** - All processing on backend (transcription, AI, docs)
- 📱 **Mobile-optimized** - Works on Android glasses/devices

## Quick Start

### Development

```bash
# Install dependencies
npm install

# Start backend API (in separate terminal)
cd ../src/SmartGlassesMiddleware
dotnet run

# Start Vite dev server
npm run dev
```

Visit **http://localhost:3000**

### Production Build

```bash
npm run build
# Output: dist/ folder (static files)

# Preview production build locally
npm run preview
```

## Architecture

```
Smart Glasses (Android)
 └─ Vite React SPA (80KB)
     ├─ Voice: capture audio → POST /api/transcribe → POST /api/chat/ask
     ├─ PDFs: GET /api/documents → display list
     └─ Health: GET /health → show system status
```

## API Integration

All API calls go to backend (proxied in dev, direct in production):

```typescript
// Voice transcription
const formData = new FormData();
formData.append('audio', audioBlob);
const res = await fetch('/api/transcribe', { method: 'POST', body: formData });

// AI chat
const res = await fetch('/api/chat/ask', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ prompt: 'Hello' }),
});

// Documents
const res = await fetch('/api/documents');
const docs = await res.json();
```

## Deployment Options

### Option 1: S3 + CloudFront (Recommended)

```bash
# Build
npm run build

# Upload to S3
aws s3 sync dist/ s3://my-bucket --delete

# Create CloudFront distribution pointing to S3
# Set API_URL in environment or hardcode backend URL
```

### Option 2: Serve from .NET app

```bash
# Build
npm run build

# Copy to .NET wwwroot
cp -r dist/* ../src/SmartGlassesMiddleware/wwwroot/
```

Then access via .NET server at http://localhost:5000

### Option 3: Android WebView

Embed in Android app:

```kotlin
// Load local or remote URL
webView.loadUrl("https://your-cloudfront-domain.com")
// or
webView.loadUrl("file:///android_asset/index.html")
```

## Environment Variables

For production, set API URL:

```bash
# .env.production
VITE_API_URL=https://api.yourdomain.com
```

Update `vite.config.ts` to use:

```typescript
define: {
  'import.meta.env.VITE_API_URL': JSON.stringify(process.env.VITE_API_URL || ''),
}
```

## Bundle Size

Production build:
- **Main bundle**: ~80KB (React + Router + code)
- **Vendor chunk**: ~40KB (React libraries)
- **Total**: ~120KB gzipped

Compare to Next.js: ~200KB+ framework overhead

## Browser Support

- Chrome/Edge (Android glasses)
- Safari (iOS fallback)
- Firefox

Requires:
- `MediaDevices.getUserMedia` for voice recording
- Modern JavaScript (ES2020+)

## Optimizations for Glasses

- Minimal bundle size (faster load on slow connections)
- Large buttons/touch targets (easy to tap)
- High contrast UI (better visibility in sunlight)
- Voice-first design (hands-free interaction)
- Efficient re-renders (battery life)
