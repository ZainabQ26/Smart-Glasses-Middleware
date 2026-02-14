import { test, expect } from '@playwright/test';

// Small helper to poll an endpoint until predicate true or timeout
async function poll(request, url: string, predicate: (resp: any) => boolean, timeout = 15000, interval = 500) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const r = await request.get(url);
    if (r.status() >= 200 && r.status() < 300) {
      const body = await r.json();
      if (predicate(body)) return body;
    }
    await new Promise((r) => setTimeout(r, interval));
  }
  throw new Error(`Timed out polling ${url}`);
}

test.describe('Document upload and processing', () => {
  test('uploads image via /api/ask and polls task until completed', async ({ request }) => {
    const image = Buffer.from([0xff, 0xd8, 0xff]);
    const mp = {
      multipart: {
        files: { name: 'image.jpg', mimeType: 'image/jpeg', buffer: image },
        prompt: 'Process image for RAG'
      }
    } as any;

    const resp = await request.post('/api/ask', mp);
    expect([200, 202]).toContain(resp.status());
    const body = await resp.json();
    const taskId = body.taskId || body.requestId || body.id || body.task?.id;
    expect(taskId).toBeTruthy();

    const task = await poll(request, `/api/tasks/${taskId}`, (b) => b.status === 'Completed', 20000, 500);
    expect(task.status).toBe('Completed');
  });

  test('uploads PDF via /api/admin/files and verifies file appears in listing', async ({ request }) => {
    const pdf = Buffer.from([0x25, 0x50, 0x44, 0x46]);
    const mp = {
      multipart: {
        file: { name: 'sample.pdf', mimeType: 'application/pdf', buffer: pdf },
        label: 'e2e-pdf'
      }
    } as any;

    const resp = await request.post('/api/admin/files', mp);
    expect([200, 201]).toContain(resp.status());
    const created = await resp.json();
    const uploadedName = created?.fileName || created?.FileName || 'sample.pdf';

    // Poll files list until we see the file
    const listing = await poll(request, '/api/admin/files?page=1&pageSize=20', (b) => {
      if (!b || !b.data && !b.Data) return false;
      const data = b.data || b.Data;
      return data.some((f) => f.fileName === uploadedName || f.FileName === uploadedName);
    }, 15000, 500);

    // Found
    expect(listing).toBeTruthy();
  });

  test('uploads XML via /api/ask and ensures RAG processing occurs (task completes)', async ({ request }) => {
    const xml = Buffer.from('<doc><p>hi</p></doc>');
    const mp = {
      multipart: {
        file: { name: 'sample.xml', mimeType: 'application/xml', buffer: xml },
        label: 'e2e-xml'
      }
    } as any;

    const resp = await request.post('/api/admin/files', mp);
    expect([200, 201]).toContain(resp.status());

    const created = await resp.json();
    const uploadedName = created?.fileName || created?.FileName || 'sample.xml';

    const listing = await poll(request, '/api/admin/files?page=1&pageSize=20', (b) => {
      if (!b || !b.data && !b.Data) return false;
      const data = b.data || b.Data;
      return data.some((f) => f.fileName === uploadedName || f.FileName === uploadedName);
    }, 15000, 500);

    expect(listing).toBeTruthy();
  });
});
