import { test, expect } from '@playwright/test';

test.describe('Backend error injection and UI mapping', () => {
  test('should render failed request details from backend response', async ({ page }) => {
    const failedPayload = [
      {
        requestId: 'failed-req-1',
        requestType: 'Media',
        description: 'Injected failure',
        status: 'Failed',
        error: 'Injected backend error',
        progress: 100,
        startTime: new Date('2026-02-11T09:59:00Z').toISOString(),
        endTime: new Date('2026-02-11T10:00:00Z').toISOString()
      }
    ];

    await page.route('**/api/monitoring/requests', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(failedPayload)
    }));

    await page.route('**/api/history', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]'
    }));

    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });

    const item = page.locator('[data-testid="request-item"]').first();
    await expect(item.locator('.request-status')).toHaveClass(/failed/);
    await expect(item.locator('.request-error')).toContainText('Injected backend error');
  });

  test('should return ProblemDetails for invalid /api/ask payload', async ({ request }) => {
    const response = await request.post('/api/ask', {
      multipart: {
        Files: {
          name: 'bad.exe',
          mimeType: 'application/octet-stream',
          buffer: Buffer.from([0x4d, 0x5a])
        },
        Prompt: 'invalid file type'
      }
    });

    expect(response.status()).toBeGreaterThanOrEqual(400);
    expect(response.status()).toBeLessThan(500);

    const contentType = response.headers()['content-type'] || '';
    expect(contentType).toContain('application/problem+json');
  });
});
