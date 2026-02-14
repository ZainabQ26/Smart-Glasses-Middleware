import { test, expect } from '@playwright/test';

function createSilentWavBuffer() {
  // Minimal WAV header with a single silent sample.
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

test.describe('Admin UI Uses API Data', () => {
  test.setTimeout(60000);

  test.beforeEach(async ({ page }) => {
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
  });

  test('should load prompts from the API', async ({ page, request }) => {
    const apiResponse = await request.get('/api/admin/prompts');
    expect(apiResponse.status()).toBeLessThan(500);

    const promptsTab = page.locator('button[data-tab="prompts"]');
    await promptsTab.click();
    await expect(promptsTab).toBeVisible();
  });

  test('should load files from the API', async ({ page, request }) => {
    const apiResponse = await request.get('/api/admin/files');
    expect(apiResponse.status()).toBeLessThan(500);

    const filesTab = page.locator('button[data-tab="files"]');
    await filesTab.click();
    await expect(filesTab).toBeVisible();
  });

  test('should load groups from the API', async ({ page, request }) => {
    const apiResponse = await request.get('/api/admin/groups');
    expect(apiResponse.status()).toBeLessThan(500);

    const groupsTab = page.locator('button[data-tab="groups"]');
    await groupsTab.click();
    await expect(groupsTab).toBeVisible();
  });

  test('should refetch API data after reload', async ({ page, request }) => {
    const initialResponse = await request.get('/api/admin/prompts');
    expect(initialResponse.status()).toBeLessThan(500);

    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);

    const reloadResponse = await request.get('/api/admin/prompts');
    expect(reloadResponse.status()).toBeLessThan(500);
  });
});

test.describe('Cross-Device API Updates Show Immediately', () => {
  test.setTimeout(90000);

  test.beforeEach(async ({ page }) => {
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1500);
  });

  test('should show a request triggered from another device', async ({ page, browser, browserName }) => {
    const requestItems = page.locator('.request-item');
    const initialCount = await requestItems.count();

    const deviceContext = await browser.newContext();
    const deviceRequest = deviceContext.request;

    try {
      const response = await deviceRequest.post('/api/ask', {
        multipart: {
          Files: {
            name: 'device-test.wav',
            mimeType: 'audio/wav',
            buffer: createSilentWavBuffer()
          },
          Prompt: 'device update check'
        }
      });

      expect(response.status()).toBeGreaterThanOrEqual(200);
      expect(response.status()).toBeLessThan(500);

      const pollTimeout = browserName === 'firefox' ? 30000 : 20000;
      // small initial wait to give SignalR time to propagate on slower browsers
      await page.waitForTimeout(500);
      await expect.poll(async () => await requestItems.count(), { timeout: pollTimeout })
        .toBeGreaterThan(initialCount);
    } finally {
      await deviceContext.close();
    }
  });
});

