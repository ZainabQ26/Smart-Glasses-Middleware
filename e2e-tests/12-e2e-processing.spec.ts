import { test, expect } from '@playwright/test';

function createSilentWavBuffer() {
  const sampleRate = 8000;
  const numChannels = 1;
  const bitsPerSample = 8;
  const numSamples = 1;
  const blockAlign = (numChannels * bitsPerSample) / 8;
  const byteRate = sampleRate * blockAlign;
  const dataSize = numSamples * blockAlign;
  const buffer = Buffer.alloc(44 + dataSize);

  buffer.write('RIFF', 0);
  buffer.writeUInt32LE(36 + dataSize, 4);
  buffer.write('WAVE', 8);
  buffer.write('fmt ', 12);
  buffer.writeUInt32LE(16, 16);
  buffer.writeUInt16LE(1, 20);
  buffer.writeUInt16LE(numChannels, 22);
  buffer.writeUInt32LE(sampleRate, 24);
  buffer.writeUInt32LE(byteRate, 28);
  buffer.writeUInt16LE(blockAlign, 32);
  buffer.writeUInt16LE(bitsPerSample, 34);
  buffer.write('data', 36);
  buffer.writeUInt32LE(dataSize, 40);
  buffer.writeUInt8(128, 44);

  return buffer;
}

test.describe('Full E2E create → process → monitoring flow', () => {
  test.setTimeout(120000);

  test('should create a task and expose it in monitoring', async ({ page, request }) => {
    const response = await request.post('/api/ask', {
      multipart: {
        Files: {
          name: 'e2e-flow.wav',
          mimeType: 'audio/wav',
          buffer: createSilentWavBuffer()
        },
        Prompt: 'e2e flow test'
      }
    });

    expect(response.status()).toBeGreaterThanOrEqual(200);
    expect(response.status()).toBeLessThan(300);

    const body = await response.json();
    expect(body.requestId).toBeTruthy();

    const requestId = body.requestId as string;

    await expect.poll(async () => {
      const taskResponse = await request.get(`/api/tasks/${requestId}`);
      if (taskResponse.status() === 202) {
        const data = await taskResponse.json();
        return data.status || 'Processing';
      }
      if (taskResponse.status() === 200) {
        const data = await taskResponse.json();
        return data.status || 'Completed';
      }
      return 'Unknown';
    }, { timeout: 60000 }).toMatch(/Completed|Failed|Processing/);

    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });

    const item = page.locator(`[data-testid="request-item"][data-request-id="${requestId}"]`);
    await expect(item).toBeVisible({ timeout: 30000 });

    await expect(item.locator('.request-status')).toBeVisible();
  });
});
