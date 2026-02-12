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

test.describe('Concurrency, load, and ordering', () => {
  test.setTimeout(120000);

  test('should accept concurrent submissions and surface them in monitoring', async ({ request, page }) => {
    const baselineResponse = await request.get('/api/monitoring/requests?count=1000');
    const baseline = baselineResponse.status() === 200 ? (await baselineResponse.json()).length : 0;

    const prompts = ['concurrency-1', 'concurrency-2', 'concurrency-3'];

    await Promise.all(prompts.map(prompt => request.post('/api/ask', {
      multipart: {
        Files: {
          name: `${prompt}.wav`,
          mimeType: 'audio/wav',
          buffer: createSilentWavBuffer()
        },
        Prompt: prompt
      }
    })));

    await expect.poll(async () => {
      const response = await request.get('/api/monitoring/requests?count=1000');
      if (response.status() !== 200) return baseline;
      const data = await response.json();
      return data.length;
    }, { timeout: 30000 }).toBeGreaterThanOrEqual(baseline + prompts.length);

    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });

    await expect.poll(async () => page.locator('[data-testid="request-item"]').count(), { timeout: 15000 })
      .toBeGreaterThan(0);
  });
});
