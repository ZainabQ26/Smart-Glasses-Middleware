import { test, expect } from '@playwright/test';

test.describe('UI Features and Consistency', () => {

  test.describe('Monitoring Page Features', () => {
    test.beforeEach(async ({ page }) => {
      // Mock the history API to return data with transcription logs
      await page.route('**/api/history*', async route => {
        const json = [
          {
            "requestId": "req-1",
            "correlationId": "req-1",
            "requestType": "transcription",
            "inputText": "Hello world transcription",
            "response": "Transcription completed",
            "startTime": new Date().toISOString(),
            "endTime": new Date().toISOString(),
            "success": true
          },
          {
            "requestId": "req-2",
            "correlationId": "req-1",
            "requestType": "ai",
            "inputText": "Process this text",
            "response": "Processed output",
            "startTime": new Date().toISOString(),
            "endTime": new Date().toISOString(),
            "success": true
          }
        ];
        await route.fulfill({ json });
      });

      // Mock active requests to be empty or link to history
      await page.route('**/api/monitoring/requests*', async route => {
        await route.fulfill({ json: [] });
      });

      await page.goto('/monitoring.html');
      // Wait for initial load
      await page.waitForTimeout(500);
    });

    test('should show transcription and request logs', async ({ page }) => {
      // Find the request item
      const requestItem = page.locator('[data-request-id="req-1"]');
      await expect(requestItem).toBeVisible();

      // Check for the transcription text in the description or expanded details
      const description = requestItem.locator('.request-description');
      
      // The logic in monitoring.html shows the FIRST history item's inputText as description if synthesized
      // "Hello world transcription"
      await expect(description).toContainText('Hello world transcription');

      // Expand details if they are collapsed
      const detailsSummary = requestItem.locator('summary');
      if (await detailsSummary.isVisible()) {
        await detailsSummary.click();
      }

      // Check for log entries
      const logs = requestItem.locator('.history-entry');
      await expect(logs).toHaveCount(2);

      // Verify log content
      await expect(logs.nth(0)).toContainText('transcription');
      await expect(logs.nth(1)).toContainText('Processed output');
    });
  });

  test.describe('Admin Page Features', () => {
    test.beforeEach(async ({ page }) => {
      await page.goto('/admin.html');
    });

    test('should tabs on admin page working and showing the correct data', async ({ page }) => {
      // Check Files Tab
      const fileTab = page.locator('[data-testid="tab-files"]');
      await fileTab.click();
      await expect(page.locator('#files')).toBeVisible();
      await expect(page.locator('#prompts')).not.toBeVisible();
      await expect(page.locator('#reminders')).not.toBeVisible();
      await expect(page.locator('#groups')).not.toBeVisible();

      // Check Prompts/Conversations Tab
      const promptTab = page.locator('[data-testid="tab-prompts"]');
      await promptTab.click();
      await expect(page.locator('#prompts')).toBeVisible();
      await expect(page.locator('#files')).not.toBeVisible();
      await expect(page.locator('#reminders')).not.toBeVisible();
      await expect(page.locator('#groups')).not.toBeVisible();

      // Check Groups Tab
      const groupsTab = page.locator('[data-testid="tab-groups"]');
      await groupsTab.click();
      await expect(page.locator('#groups')).toBeVisible();
      await expect(page.locator('#files')).not.toBeVisible();
      await expect(page.locator('#prompts')).not.toBeVisible();
      await expect(page.locator('#reminders')).not.toBeVisible();
    });

    test('should toggle dark mode and persist consistency', async ({ page }) => {
      const toggle = page.locator('.theme-toggle');
      
      // Initial state (assume light)
      await expect(page.locator('body')).not.toHaveClass(/dark/);
      
      // Click to toggle Dark Mode
      await toggle.click();
      
      // Check body class
      await expect(page.locator('body')).toHaveClass(/dark/);
      
      // Check background color (should be dark)
      const bgColor = await page.evaluate(() => {
        return window.getComputedStyle(document.body).backgroundColor;
      });
      // #1a1f2e is rgb(26, 31, 46)
      expect(bgColor).toBe('rgb(26, 31, 46)');

      // Toggle back
      await toggle.click();
      await expect(page.locator('body')).not.toHaveClass(/dark/);
    });

    test('formatting matching throughout the program (font-family)', async ({ page }) => {
      const fontFamily = await page.evaluate(() => {
        return window.getComputedStyle(document.body).fontFamily;
      });
      // Check for common fonts in the stack
      expect(fontFamily).toContain('Segoe UI');
      expect(fontFamily).toContain('sans-serif');
      
      // Check consistency with monitoring page
      await page.goto('/monitoring.html');
      const monitorFont = await page.evaluate(() => {
        return window.getComputedStyle(document.body).fontFamily;
      });
      expect(monitorFont).toBe(fontFamily);
    });

    test('spacing between elements', async ({ page }) => {
      // Ensure cards have spacing
      const cards = page.locator('.card');
      const count = await cards.count();
      
      if (count >= 2) {
        const first = await cards.nth(0).boundingBox();
        const second = await cards.nth(1).boundingBox();
        
        // Ensure second starts after first ends with some gap
        if (first && second) {
          const gap = second.y - (first.y + first.height);
          expect(gap).toBeGreaterThan(10); // Expect at least 10px gap
        }
      }
    });
  });

  test.describe('Responsive Design (different screen sizes)', () => {
    test.use({ viewport: { width: 375, height: 667 } }); // Mobile

    test('admin page adapts to mobile', async ({ page }) => {
      await page.goto('/admin.html');
      
      // In mobile, tabs usually stack or scroll. 
      // Verify container padding is smaller or layout adjusts
      const container = page.locator('.container');
      await expect(container).toBeVisible();
      
      // Check specific mobile adjustments if known, otherwise just ensure no horizontal scroll on body
      const scrollWidth = await page.evaluate(() => document.body.scrollWidth);
      const viewportWidth = await page.evaluate(() => window.innerWidth);
      
      // Allow slight variance for scrollbars
      expect(scrollWidth).toBeLessThanOrEqual(viewportWidth + 1);
    });
  });
});
