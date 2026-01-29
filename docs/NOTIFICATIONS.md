# Notifications: SignalR + Redis Pub/Sub

This project supports two notification paths for background task completion:

- SignalR hub: `/hubs/tasks` — browser/JS clients connect here to receive real-time `TaskCompleted` events.
- Redis Pub/Sub: `background:task:completed` — useful for non-browser subscribers or external services.

SignalR browser example

Open `samples/signalr-client.html` in a browser (or serve it from your frontend). Connects to the hub at `/hubs/tasks` and listens for `TaskCompleted` messages.

Node.js Redis subscriber

A simple Node.js subscriber is provided at `scripts/redis-subscriber.js` (uses `ioredis`). Run:

```bash
npm install ioredis
node scripts/redis-subscriber.js
```

Health checks

The app's `/health` endpoint now includes a `redis-pubsub-bridge` check. This reports whether the server-side Redis bridge successfully subscribed and forwarded messages to SignalR.

Configuration

The TTL for completed task keys (so clients can poll after a missed notification) is configurable in `appsettings.json` under `BackgroundTasks:RetentionSeconds` (default `60`).
