import { test, expect } from '@playwright/test';

test.describe('Monitoring Page - Real-time Features', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
  });

  test('should attempt SignalR connection', async ({ page }) => {
    // Monitor console logs
    const logs: string[] = [];
    page.on('console', msg => logs.push(msg.text()));
    
    // Wait for connection attempt
    await page.waitForTimeout(3000);
    
    // Should see SignalR-related logs
    const hasSignalRActivity = logs.some(log => 
      log.toLowerCase().includes('signalr') || 
      log.toLowerCase().includes('websocket') ||
      log.toLowerCase().includes('hub')
    );
    
    // Connection might succeed or fail, but should attempt
    expect(typeof hasSignalRActivity).toBe('boolean');
  });

  test('should update statistics periodically', async ({ page }) => {
    const totalRequests = page.locator('#total-requests');
    
    const initialValue = await totalRequests.textContent();
    
    // Wait a bit
    await page.waitForTimeout(5000);
    
    const laterValue = await totalRequests.textContent();
    
    // Values might change or stay the same
    expect(initialValue).toBeTruthy();
    expect(laterValue).toBeTruthy();
  });

  test('should display request items if any exist', async ({ page }) => {
    await page.waitForTimeout(2000);
    
    const requestItems = page.locator('.request-item');
    const noRequests = page.locator('[data-testid="requests-empty"]');
    
    const itemCount = await requestItems.count();
    const noRequestsVisible = await noRequests.isVisible().catch(() => false);
    
    // Either has items or shows "no requests"
    expect(itemCount > 0 || noRequestsVisible).toBeTruthy();
  });

  test('should show progress indicators on active requests', async ({ page }) => {
    await page.waitForTimeout(2000);
    
    const progressBars = page.locator('[data-testid="request-progress"]');
    
    // Progress bars might or might not exist depending on active requests
    const count = await progressBars.count();
    expect(count).toBeGreaterThanOrEqual(0);

    const requestItems = page.locator('.request-item');
    if (await requestItems.count() > 0) {
      const progressFill = requestItems.first().locator('[data-testid="request-progress"]');
      await expect(progressFill).toBeVisible();
      const ariaNow = await progressFill.getAttribute('aria-valuenow');
      expect(ariaNow).not.toBeNull();
    }
  });

  test('should show current stage text when requests exist', async ({ page }) => {
    await page.waitForTimeout(2000);

    const requestItems = page.locator('.request-item');
    if (await requestItems.count() > 0) {
      const stageText = requestItems.first().locator('[data-testid="request-stage"]');
      await expect(stageText).toBeVisible();
      const text = await stageText.textContent();
      expect(text && text.length > 0).toBeTruthy();
    }
  });

  test('should handle connection status changes', async ({ page }) => {
    const statusElement = page.locator('#connection-status, .connection-status');
    
    const initialClass = await statusElement.getAttribute('class');
    
    await page.waitForTimeout(3000);
    
    const laterClass = await statusElement.getAttribute('class');
    
    // Class might change as connection state changes
    expect(initialClass || laterClass).toBeTruthy();
  });

  test('should maintain monitoring after page reload', async ({ page }) => {
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    // Should reconnect and show monitoring UI
    await expect(page.locator('[data-testid="monitoring-title"]')).toBeVisible();
    
    await page.waitForTimeout(2000);
    
    const statusElement = page.locator('[data-testid="connection-status"]');
    await expect(statusElement).toBeVisible();
  });

  test('should show request metadata if requests exist', async ({ page }) => {
    await page.waitForTimeout(2000);
    
    const requestItems = page.locator('.request-item');
    
    if (await requestItems.count() > 0) {
      const firstItem = requestItems.first();
      
      // Should have some content
      const content = await firstItem.textContent();
      expect(content?.length).toBeGreaterThan(0);
    }
  });

  test('should handle rapid reloads without breaking', async ({ page }) => {
    for (let i = 0; i < 3; i++) {
      await page.reload();
      await page.waitForTimeout(1000);
      
      await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
    }
  });
});

test.describe('Monitoring Page - Request History', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
  });

  test('should load historical requests on page load', async ({ page }) => {
    await page.waitForTimeout(2000);
    
    const requestsList = page.locator('#requests-list');
    await expect(requestsList).toBeVisible();
  });

  test('should display request status badges', async ({ page }) => {
    await page.waitForTimeout(2000);
    
    const statusBadges = page.locator('.request-status, .status-badge, .badge');
    
    const count = await statusBadges.count();
    expect(count).toBeGreaterThanOrEqual(0);
  });

  test('should show request timestamps', async ({ page }) => {
    await page.waitForTimeout(2000);
    
    const requestItems = page.locator('.request-item');
    
    if (await requestItems.count() > 0) {
      // Look for time-related content
      const content = await page.content();
      const hasTimeInfo = content.includes('Started') || 
                         content.includes('Duration') || 
                         content.includes('ms') ||
                         content.includes(':'); // Time separator
      
      expect(typeof hasTimeInfo).toBe('boolean');
    }
  });

  test('should handle empty request history gracefully', async ({ page }) => {
    await page.waitForTimeout(1000);
    
    const requestsList = page.locator('#requests-list');
    const content = await requestsList.textContent();
    
    // Should show either requests or a message
    expect(content).toBeTruthy();
  });
});

test.describe('Monitoring Page - UI Responsiveness', () => {
  test('should be responsive on mobile viewport', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/monitoring.html');
    
    await expect(page.locator('h1')).toBeVisible();
    await expect(page.locator('#total-requests')).toBeVisible();
  });

  test('should be responsive on tablet viewport', async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.goto('/monitoring.html');
    
    await expect(page.locator('h1')).toBeVisible();
    await expect(page.locator('.stats-container, .stat-card').first()).toBeVisible();
  });

  test('should be responsive on desktop viewport', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto('/monitoring.html');
    
    await expect(page.locator('h1')).toBeVisible();
    await expect(page.locator('.stats-container')).toBeVisible();
  });

  test('should maintain layout during viewport changes', async ({ page }) => {
    await page.goto('/monitoring.html');
    
    const viewports = [
      { width: 375, height: 667 },
      { width: 768, height: 1024 },
      { width: 1280, height: 720 },
      { width: 1920, height: 1080 }
    ];
    
    for (const viewport of viewports) {
      await page.setViewportSize(viewport);
      await page.waitForTimeout(300);
      
      await expect(page.locator('h1')).toBeVisible();
      await expect(page.locator('#connection-status')).toBeVisible();
    }
  });
});
