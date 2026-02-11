# Async Document Query (202 + Polling + SignalR)

This document shows how to call the document query API in async mode and receive results via polling or SignalR.

1) Start async request (server accepts `Prefer: respond-async` or use `?async=true`):

Example (fetch):

```javascript
// send async request
const payload = { Query: "What language skills does this person have?", TopK: 5 };
const resp = await fetch('/api/documents/query?async=true', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
  body: JSON.stringify(payload)
});

if (resp.status === 202) {
  const body = await resp.json();
  const taskId = body.taskId;
  const location = body.location; // e.g. /api/tasks/{id}
  // Polling example below or use SignalR to listen for TaskCompleted
}
```

2) Polling endpoint

GET `/api/tasks/{id}` returns:
- 202 Accepted while queued/processing
- 200 OK with `{ result }` when completed
- 500 on failure

Polling example:

```javascript
async function pollTask(taskId, intervalMs = 2000) {
  const url = `/api/tasks/${taskId}`;
  while (true) {
    const r = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
    if (r.status === 202) {
      // still working
      await new Promise(res => setTimeout(res, intervalMs));
      continue;
    }
    if (r.status === 200) {
      const body = await r.json();
      return body.result;
    }
    // failure
    const err = await r.json().catch(() => ({ error: 'Unknown error' }));
    throw new Error(err.error || 'Task failed');
  }
}
```

3) SignalR (recommended)

- The server publishes `TaskCompleted` events via SignalR (and Redis pub/sub). The payload contains `{ id, status, result, error }`.

Client example (SignalR):

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/taskHub') // adjust hub URL if different
  .withAutomaticReconnect()
  .build();

connection.on('TaskCompleted', payload => {
  console.log('TaskCompleted', payload);
  // If payload.id === taskId, show results
});

await connection.start();
```

Notes
- The frontend already uses an AbortController in `wwwroot/admin.js` with a 150s timeout for the inline query flow; that will not abort at ~60s.
- If you still observe ~60s cancellations, check any reverse proxy (nginx, Traefik) or container platform timeout for request/response (e.g. Traefik/ECS/ALB idle timeouts).

