import { test, expect } from '@playwright/test';

// Verifies that the processed PDF is available via the admin preview endpoint
test('processed document is previewable as PDF via admin preview', async ({ request }) => {
  const textContent = 'Preview test document content for PDF generation.';
  const mp = {
    multipart: {
      file: { name: 'preview-test.txt', mimeType: 'text/plain', buffer: Buffer.from(textContent) }
    }
  } as any;

  const uploadResp = await request.post('/api/documents/upload', mp);
  expect([200,201]).toContain(uploadResp.status());
  const uploadBody = await uploadResp.json();
  const documentId = uploadBody.id || uploadBody.Id;
  expect(documentId).toBeTruthy();

  // Wait briefly for processing to complete
  await new Promise((r) => setTimeout(r, 2500));

  // Fetch document metadata to get ManagedFileId
  const docResp = await request.get(`/api/documents/${documentId}`);
  expect(docResp.ok()).toBeTruthy();
  const doc = await docResp.json();
  expect(doc.isProcessed).toBeTruthy();
  console.log('Document metadata:', JSON.stringify(doc));
  const managedFileId = doc.managedFileId || doc.ManagedFileId || doc.managedFile?.id;
  expect(managedFileId).toBeTruthy();

  // Call admin preview endpoint
  const previewResp = await request.get(`/api/admin/files/${managedFileId}/preview`);
  expect(previewResp.ok()).toBeTruthy();

  const ct = previewResp.headers()['content-type'] || '';
  expect(ct).toContain('pdf');

  const body = await previewResp.body();
  // Check PDF magic bytes '%PDF' at start
  const header = body.slice(0,4).toString('utf8');
  expect(header).toBe('%PDF');
});
