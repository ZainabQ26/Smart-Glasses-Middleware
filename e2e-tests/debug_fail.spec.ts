import { test, expect } from '@playwright/test';

test('debug monitoring request rendering', async ({ page }) => {
    const failedPayload = [
      {
        requestId: 'failed-req-1',
        requestType: 'Media',
        description: 'Injected failure',
        status: 'Failed',
        error: 'Injected backend error',
        progress: 100,
        success: false,
        startTime: new Date().toISOString(),
        endTime: new Date().toISOString()
      }
    ];

    await page.route('**/api/monitoring/requests*', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]'
    }));

    await page.route('**/api/history*', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]'
    }));

    await page.goto('/monitoring.html');
    await page.waitForFunction(() => Boolean((window as any).__monitoringTestHooks));
    await page.evaluate((payload) => {
      (window as any).__monitoringTestHooks.setRequests(payload);
    }, failedPayload);

    const item = page.locator('[data-testid="request-item"]').first();
    await expect(item).toBeVisible();
    await expect(item.locator('.request-status')).toHaveClass(/failed/);
    await expect(item.locator('.request-status')).toContainText(/failed/i);
});
