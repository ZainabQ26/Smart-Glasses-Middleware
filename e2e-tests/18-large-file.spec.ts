import { test, expect } from '@playwright/test';

test.describe('Large-file and long-process handling', () => {
  test.setTimeout(180000);

  test('should accept a large upload and expose task status', async ({ request }) => {
    test.skip(!process.env.E2E_RUN_LONG, 'E2E_RUN_LONG not set');

    const sizeBytes = 2 * 1024 * 1024;
    const buffer = Buffer.alloc(sizeBytes, 0);

    const response = await request.post('/api/ask', {
      multipart: {
        Files: {
          name: 'large-audio.wav',
          mimeType: 'audio/wav',
          buffer
        },
        Prompt: 'large file test'
      }
    });

    expect(response.status()).toBeGreaterThanOrEqual(200);
    expect(response.status()).toBeLessThan(300);

    const body = await response.json();
    expect(body.requestId).toBeTruthy();

    const taskResponse = await request.get(`/api/tasks/${body.requestId}`);
    expect(taskResponse.status()).toBeGreaterThanOrEqual(200);
    expect(taskResponse.status()).toBeLessThan(300);
  });
});
