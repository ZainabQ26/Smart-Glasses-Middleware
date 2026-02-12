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
        startTime: new Date().toISOString(),
        endTime: new Date().toISOString()
      }
    ];

    await page.route('**/api/monitoring/requests*', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(failedPayload)
    }));

    await page.route('**/api/history*', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]'
    }));

    await page.goto('/monitoring.html');
    await page.waitForTimeout(2000); // Wait for JS

    const item = page.locator('[data-testid="request-item"]').first();
    // Check if item exists
    if (await item.count() === 0) {
        console.log('No request item found!');
        const html = await page.content();
        console.log(html);
        return;
    }
    
    const status = await item.locator('.request-status').textContent();
    const classes = await item.locator('.request-status').getAttribute('class');
    
    console.log('Status Text:', status);
    console.log('Classes:', classes);
    
    expect(status).toContain('Failed');
});
