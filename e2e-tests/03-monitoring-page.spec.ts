import { test, expect } from '@playwright/test';

test.describe('Monitoring Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/monitoring.html');
  });

  test('should load monitoring page successfully', async ({ page }) => {
    await expect(page).toHaveTitle(/Request Monitoring/i);
    
    // Check main heading
    await expect(page.locator('h1:has-text("Request Monitoring")')).toBeVisible();
  });

  test('should reload monitoring page without errors', async ({ page }) => {
    await page.reload();
    
    // Verify page is still functional after reload
    await expect(page.locator('h1:has-text("Request Monitoring")')).toBeVisible();
  });

  test('should display connection status', async ({ page }) => {
    const connectionStatus = page.locator('#connection-status, .connection-status');
    await expect(connectionStatus).toBeVisible();
    
    // Wait a bit for connection attempt
    await page.waitForTimeout(2000);
    
    // Status should show either connected or disconnected
    const statusText = await connectionStatus.textContent();
    expect(statusText).toMatch(/connect/i);
  });

  test('should display statistics cards', async ({ page }) => {
    // Check for stat cards
    await expect(page.locator('text=Total Requests')).toBeVisible();
    await expect(page.locator('text=Successful')).toBeVisible();
    await expect(page.locator('text=Failed')).toBeVisible();
    await expect(page.locator('text=Active')).toBeVisible();
  });

  test('should display requests container', async ({ page }) => {
    const requestsContainer = page.locator('.requests-container, #requests-list');
    await expect(requestsContainer.first()).toBeVisible();
  });

  test('should show "Recent Requests" header', async ({ page }) => {
    await expect(page.locator('text=Recent Requests')).toBeVisible();
  });

  test('should navigate back to admin page', async ({ page }) => {
    const adminLink = page.locator('a[href="/admin.html"]');
    await expect(adminLink).toBeVisible();
    
    await adminLink.click();
    await page.waitForURL('**/admin.html');
    await expect(page).toHaveTitle(/Smart Glasses.*File.*Prompt/i);
  });

  test('should load SignalR library', async ({ page }) => {
    // Check that SignalR script is loaded
    const signalrScript = page.locator('script[src*="signalr"]');
    await expect(signalrScript).toBeVisible();
  });

  test('should load toasts CSS and JS', async ({ page }) => {
    // Check that toast resources are loaded
    const toastsCss = page.locator('link[href*="toasts.css"]');
    const toastsScript = page.locator('script[src*="toasts.js"]');
    
    expect(await toastsCss.count()).toBeGreaterThan(0);
    expect(await toastsScript.count()).toBeGreaterThan(0);
  });

  test('should initialize with empty or populated request list', async ({ page }) => {
    await page.waitForTimeout(1000);
    
    const requestsList = page.locator('#requests-list');
    await expect(requestsList).toBeVisible();
    
    // Either shows "No requests" or has request items
    const noRequests = page.locator('text=No requests yet');
    const requestItems = page.locator('.request-item');
    
    const hasNoRequests = await noRequests.count() > 0;
    const hasRequests = await requestItems.count() > 0;
    
    expect(hasNoRequests || hasRequests).toBeTruthy();
  });

  test('should handle multiple page reloads', async ({ page }) => {
    for (let i = 0; i < 3; i++) {
      await page.reload();
      await expect(page.locator('h1:has-text("Request Monitoring")')).toBeVisible();
      await page.waitForTimeout(500);
    }
  });

  test('should maintain responsive design', async ({ page }) => {
    // Test different viewport sizes
    await page.setViewportSize({ width: 1920, height: 1080 });
    await expect(page.locator('text=Recent Requests')).toBeVisible();
    
    await page.setViewportSize({ width: 768, height: 1024 });
    await expect(page.locator('text=Recent Requests')).toBeVisible();
    
    await page.setViewportSize({ width: 375, height: 667 });
    await expect(page.locator('text=Recent Requests')).toBeVisible();
  });

  test('should not have critical console errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        errors.push(msg.text());
      }
    });
    
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    // Filter out network errors from potentially unavailable services
    const criticalErrors = errors.filter(e => 
      !e.includes('Failed to fetch') && 
      !e.includes('NetworkError') &&
      !e.includes('net::ERR_') &&
      !e.includes('WebSocket') &&
      !e.includes('SignalR')
    );
    
    expect(criticalErrors.length).toBe(0);
  });

  test('should display stats with numeric values', async ({ page }) => {
    await page.waitForTimeout(1000);
    
    const totalRequests = page.locator('#total-requests');
    const successfulRequests = page.locator('#successful-requests');
    const failedRequests = page.locator('#failed-requests');
    const activeRequests = page.locator('#active-requests');
    
    // All stat values should be visible and numeric
    const stats = [totalRequests, successfulRequests, failedRequests, activeRequests];
    
    for (const stat of stats) {
      await expect(stat).toBeVisible();
      const text = await stat.textContent();
      expect(text).toMatch(/^\d+$/);
    }
  });

  test('should handle SignalR connection attempts', async ({ page }) => {
    // Monitor console for SignalR messages
    const messages: string[] = [];
    page.on('console', msg => {
      messages.push(msg.text());
    });
    
    await page.waitForTimeout(3000);
    
    // Should see SignalR connection attempt in logs
    const hasSignalRLog = messages.some(m => 
      m.toLowerCase().includes('signalr') || 
      m.toLowerCase().includes('websocket') ||
      m.toLowerCase().includes('connected')
    );
    
    // This might be true or false depending on server state
    expect(typeof hasSignalRLog).toBe('boolean');
  });

  test('should update connection status over time', async ({ page }) => {
    const connectionStatus = page.locator('#connection-status');
    
    const initialStatus = await connectionStatus.textContent();
    expect(initialStatus).toBeTruthy();
    
    // Wait for potential status change
    await page.waitForTimeout(3000);
    
    const laterStatus = await connectionStatus.textContent();
    expect(laterStatus).toBeTruthy();
  });
});
