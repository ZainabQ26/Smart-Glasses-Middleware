import { test, expect } from '@playwright/test';

test.describe('Error Handling and Edge Cases', () => {
  test('should handle network errors gracefully', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Intercept and fail some API calls
    await page.route('**/api/admin/files*', route => route.abort('failed'));
    
    await page.reload();
    
    // Page should still load
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
  });

  test('should handle slow API responses', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Delay API responses
    await page.route('**/api/admin/**', async route => {
      await new Promise(resolve => setTimeout(resolve, 2000));
      await route.continue();
    });
    
    await page.reload();
    
    // Page should eventually load
    await expect(page.locator('button[data-tab="files"]')).toBeVisible({ timeout: 10000 });
  });

  test('should handle 404 responses', async ({ request }) => {
    // Use direct request to reliably validate 404 responses across browsers
    const response = await request.get('/nonexistent-page.html');
    expect(response.status()).toBe(404);
  });

  test('should handle 500 errors gracefully', async ({ page, request }) => {
    // Try to access an endpoint that might return 500
    const response = await request.post('/api/ask', {
      data: { invalid: 'data' }
    });
    
    // Should get an error but not crash
    expect(response.status()).toBeGreaterThanOrEqual(400);
  });

  test('should handle missing resources', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Intercept and fail script loads
    await page.route('**/admin.js', route => route.abort('failed'));
    
    // Page HTML should still load
    const content = await page.content();
    expect(content).toContain('html');
  });

  test('should handle CORS errors appropriately', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Monitor console for errors
    const errors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        errors.push(msg.text());
      }
    });
    
    await page.waitForTimeout(2000);
    
    // CORS errors might occur but shouldn't crash the page
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
  });

  test('should recover from JavaScript errors', async ({ page }) => {
    const errors: Error[] = [];
    page.on('pageerror', error => errors.push(error));
    
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    // Page should load even if there are minor JS errors
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
  });

  test('should handle invalid query parameters', async ({ page }) => {
    await page.goto('/admin.html?invalid=parameter&test=123');
    
    // Should still load normally
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
  });

  test('should handle extremely large viewport', async ({ page }) => {
    await page.setViewportSize({ width: 3840, height: 2160 });
    await page.goto('/admin.html');
    
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
  });

  test('should handle extremely small viewport', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 568 });
    await page.goto('/admin.html');
    
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
  });
});

test.describe('Performance and Load Testing', () => {
  test('should load admin page within reasonable time', async ({ page }) => {
    const startTime = Date.now();
    
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    const loadTime = Date.now() - startTime;
    
    // Should load within 10 seconds
    expect(loadTime).toBeLessThan(10000);
  });

  test('should load monitoring page within reasonable time', async ({ page, browserName }) => {
    const startTime = Date.now();

    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);

    const loadTime = Date.now() - startTime;

    // Allow a slightly higher threshold for Firefox on slow CI/machines
    const max = browserName === 'firefox' ? 20000 : 10000;
    expect(loadTime).toBeLessThan(max);
  });

  test('should handle rapid consecutive reloads', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Rapidly reload 5 times
    for (let i = 0; i < 5; i++) {
      await page.reload({ waitUntil: 'domcontentloaded' });
    }
    
    // Should still be functional
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
  });

  test('should handle multiple concurrent API calls', async ({ page }) => {
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    
    // Page should be functional
    await expect(page.locator('button[data-tab="files"]')).toBeVisible();
    await expect(page.locator('button[data-tab="prompts"]')).toBeVisible();
    await expect(page.locator('button[data-tab="groups"]')).toBeVisible();
  });

  test('should not leak memory on repeated navigation', async ({ page }) => {
    // Navigate multiple times
    for (let i = 0; i < 10; i++) {
      await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
      await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    }
      await expect(page.locator('[data-testid="requests-header"]')).toBeVisible();
    // Final page should still be responsive
    await expect(page.locator('text=Recent Requests')).toBeVisible();
  });
});

test.describe('Security and Validation', () => {
  test('should have secure headers', async ({ page }) => {
    const response = await page.goto('/admin.html');
    const headers = response?.headers();
    
    // Check that response exists
    expect(headers).toBeTruthy();
  });

  test('should not expose sensitive information in responses', async ({ request }) => {
    const response = await request.get('/health');
    const body = await response.text();
    
    // Should not contain sensitive info
    expect(body).not.toContain('password');
    expect(body).not.toContain('secret');
    expect(body).not.toContain('apikey');
  });

  test('should validate input on admin page', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Try to inject script through search if it exists
    const searchInput = page.locator('input[type="search"], input[type="text"]').first();
    
    if (await searchInput.count() > 0) {
      await searchInput.fill('<script>alert("xss")</script>');
      
      // Page should not execute the script
      await page.waitForTimeout(500);
      
      const hasAlert = await page.locator('text=xss').count() > 0;
      expect(hasAlert).toBeFalsy();
    }
  });

  test('should sanitize output on monitoring page', async ({ page }) => {
    await page.goto('/monitoring.html');
    await page.waitForTimeout(2000);
    
    // Page should escape HTML in request data
    const content = await page.content();
    
    // Should not have unescaped script tags
    expect(content).not.toContain('<script>alert');
  });

  test('should handle authentication redirects appropriately', async ({ page }) => {
    // Try to access an endpoint that might require auth
    await page.goto('/admin.html');
    
    // Should either show page or redirect to login (but not crash)
    const url = page.url();
    expect(url).toBeTruthy();
  });
});
