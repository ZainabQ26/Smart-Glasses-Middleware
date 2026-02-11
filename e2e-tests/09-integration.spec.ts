import { test, expect } from '@playwright/test';

test.describe('Full User Journey - Admin Workflow', () => {
  test('should complete full admin workflow', async ({ page }) => {
    // 1. Navigate to admin page
    await page.goto('/admin.html');
    await expect(page).toHaveTitle(/Smart Glasses.*File.*Prompt/i);
    
    // 2. Wait for all sections to load
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(1000);
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    await expect(page.locator('button[data-tab="prompts"]')).toBeVisible();
    await expect(page.locator('button[data-tab="groups"]')).toBeVisible();
    
    // 3. Reload to test persistence
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    // 4. Verify all sections still visible
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    await expect(page.locator('button[data-tab="prompts"]')).toBeVisible();
    await expect(page.locator('button[data-tab="groups"]')).toBeVisible();
    
    // 5. Navigate to monitoring
    const monitoringLink = page.locator('a[href="/monitoring.html"]').first();
    if (await monitoringLink.count() > 0) {
      await monitoringLink.click();
      await page.waitForURL('**/monitoring.html');
      
      // 6. Verify monitoring page loads
      await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
      
      // 7. Reload monitoring page
      await page.reload({ waitUntil: 'domcontentloaded' });
      await page.waitForTimeout(1000);
      await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
      
      // 8. Navigate back to admin
      await page.goBack();
      await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    }
  });
});

test.describe('Full User Journey - Monitoring and Real-time Updates', () => {
  test('should monitor system in real-time', async ({ page }) => {
    // 1. Navigate to monitoring page
    await page.goto('/monitoring.html');
    await expect(page).toHaveTitle(/Request Monitoring/i);
    
    // 2. Check initial connection status
    const connectionStatus = page.locator('#connection-status');
    await expect(connectionStatus).toBeVisible();
    
    // 3. Wait for connection attempt
    await page.waitForTimeout(3000);
    
    // 4. Verify stats are displayed
    await expect(page.locator('#total-requests')).toBeVisible();
    await expect(page.locator('#successful-requests')).toBeVisible();
    await expect(page.locator('#failed-requests')).toBeVisible();
    await expect(page.locator('#active-requests')).toBeVisible();
    
    // 5. Reload and verify reconnection
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(2000);
    
    // 6. Stats should still be visible
    await expect(page.locator('#total-requests')).toBeVisible();
    
    // 7. Navigate away and back
    await page.goto('/admin.html');
    await page.goto('/monitoring.html');
    
    // 8. Verify monitoring still works
    await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
  });
});

test.describe('Full User Journey - Multi-page Navigation', () => {
  test('should navigate between all pages seamlessly', async ({ page }) => {
    const pages = [
      { url: '/admin.html', title: /Smart Glasses.*File.*Prompt/i, marker: 'Files' },
      { url: '/monitoring.html', title: /Request Monitoring/i, marker: 'Recent Requests' },
      { url: '/health', title: '', marker: 'Healthy' }
    ];
    
    // Navigate to each page multiple times
    for (let iteration = 0; iteration < 2; iteration++) {
      for (const testPage of pages) {
        await page.goto(testPage.url);
        
        if (testPage.title) {
          await expect(page).toHaveTitle(testPage.title);
        }
        
        const content = await page.content();
        expect(content.toLowerCase()).toContain(testPage.marker.toLowerCase());
        
        // Reload each page
        await page.reload();
        
        const reloadedContent = await page.content();
        expect(reloadedContent.toLowerCase()).toContain(testPage.marker.toLowerCase());
      }
    }
  });
});

test.describe('Full User Journey - Browser Controls', () => {
  test('should handle all browser navigation controls', async ({ page }) => {
    // 1. Start at admin
    await page.goto('/admin.html');
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    
    // 2. Go to monitoring
    await page.goto('/monitoring.html');
    await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
    
    // 3. Use back button
    await page.goBack();
    await expect(page.url()).toContain('admin.html');
    
    // 4. Use forward button
    await page.goForward();
    await expect(page.url()).toContain('monitoring.html');
    
    // 5. Reload
    await page.reload();
    await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
    
    // 6. Hard reload (clear cache and reload)
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
    
    // 7. Navigate via URL
    await page.goto('/health');
    const content = await page.content();
    expect(content).toContain('Healthy');
    
    // 8. Back to monitoring
    await page.goto('/monitoring.html');
    await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
  });
});

test.describe('Full User Journey - Viewport Changes', () => {
  test('should work across different devices', async ({ page }) => {
    const viewports = [
      { name: 'Mobile', width: 375, height: 667 },
      { name: 'Tablet', width: 768, height: 1024 },
      { name: 'Laptop', width: 1366, height: 768 },
      { name: 'Desktop', width: 1920, height: 1080 }
    ];
    
    for (const viewport of viewports) {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      
      // Test admin page
      await page.goto('/admin.html');
      await expect(page.locator('button[data-tab="files"]')).toBeVisible();
      
      // Test monitoring page
      await page.goto('/monitoring.html');
      await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
      
      // Test reload on this viewport
      await page.reload();
      await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
    }
  });
});

test.describe('Full User Journey - API Integration', () => {
  test('should interact with API endpoints through UI', async ({ page, request }) => {
    // 1. Check health
    const healthResponse = await request.get('/health');
    expect(healthResponse.status()).toBe(200);
    
    // 2. Load admin page which calls multiple APIs
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    // 3. Verify UI loaded from API data
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    
    // 4. Load monitoring page which calls APIs
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    // 5. Verify monitoring UI loaded
    await expect(page.locator('#total-requests')).toBeVisible();
    
    // 6. Check history API directly
    const historyResponse = await request.get('/api/history');
    expect(historyResponse.status()).toBeLessThan(500);
  });
});

test.describe('Full User Journey - Error Recovery', () => {
  test('should recover from temporary network issues', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Simulate network failure
    await page.route('**/api/admin/**', route => route.abort('failed'));
    
    await page.reload();
    
    // Page should still render
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    
    // Remove network failure
    await page.unroute('**/api/admin/**');
    
    // Reload should now work
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    // Page should be fully functional
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
  });

  test('should handle service degradation gracefully', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Slow down API responses
    await page.route('**/api/**', async route => {
      await new Promise(resolve => setTimeout(resolve, 1000));
      await route.continue();
    });
    
    await page.reload();
    
    // Page should eventually load (with longer timeout)
    await expect(page.locator('button[data-tab="files"]')).toBeVisible({ timeout: 15000 });
    
    // Remove slowdown
    await page.unroute('**/api/**');
  });
});

test.describe('Full User Journey - Session Persistence', () => {
  test('should maintain functionality across session', async ({ page }) => {
    // Simulate a user session
    
    // 1. Visit admin page
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    
    // 2. Wait a bit (simulating user reading)
    await page.waitForTimeout(2000);
    
    // 3. Go to monitoring
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
    
    // 4. Wait more (simulating user watching monitoring)
    await page.waitForTimeout(3000);
    
    // 5. Return to admin
    await page.goto('/admin.html');
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    
    // 6. Reload admin
    await page.reload();
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    
    // 7. Session should still be functional
    await page.goto('/monitoring.html');
    await expect(page.locator('h1, h2, h3').filter({ hasText: /Recent.*Requests|Monitoring|Request/i }).first()).toBeVisible();
  });
});
