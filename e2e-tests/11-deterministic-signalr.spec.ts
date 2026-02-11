import { test, expect } from '@playwright/test';

test.describe('Deterministic UI updates via mocked server endpoints', () => {
  test('should surface request state from monitoring API', async ({ page }) => {
    // Mock the API endpoints to return test data
    await page.route('**/api/history', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { 
          id: 'hist-1', 
          requestType: 'Audio', 
          inputText: 'test audio', 
          detectedTask: 'general', 
          response: 'Test response', 
          usedAdvancedModel: false, 
          mediaStorageKey: null, 
          timestamp: new Date().toISOString() 
        }
      ])
    }));

    await page.route('**/api/monitoring/requests', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { 
          requestId: 'req-1', 
          requestType: 'Audio', 
          description: 'Test request', 
          status: 'Completed', 
          progress: 100, 
          startTime: new Date().toISOString(), 
          endTime: new Date(Date.now() + 5000).toISOString() 
        }
      ])
    }));

    // Navigate to monitoring page
    await page.goto('http://localhost:5000/monitoring.html', { waitUntil: 'networkidle' });
    
    // Wait for page to render request data
    await page.waitForTimeout(2000);
    
    // Verify request item renders with correct text
    const requestText = page.locator('text=Test request');
    await expect(requestText).toBeVisible({ timeout: 10000 });
  });

  test('should render error state from backend response', async ({ page }) => {
    const failedRequest = {
      requestId: 'failed-req',
      requestType: 'Media',
      description: 'Error case',
      status: 'Failed',
      error: 'Processing failed',
      progress: 100,
      startTime: new Date().toISOString(),
      endTime: new Date(Date.now() + 3000).toISOString()
    };

    await page.route('**/api/history', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]'
    }));

    await page.route('**/api/monitoring/requests', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([failedRequest])
    }));

    await page.goto('http://localhost:5000/monitoring.html', { waitUntil: 'networkidle' });
    
    await page.waitForTimeout(2000);
    
    // Verify error state renders
    const errorText = page.locator('text=Processing failed');
    await expect(errorText).toBeVisible({ timeout: 10000 });
  });
});
