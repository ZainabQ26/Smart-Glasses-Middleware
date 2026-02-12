import { test, expect } from '@playwright/test';

test.describe('SignalR Warning Bar', () => {
  test('shows warning bar when SignalR disconnected and hides on reconnect', async ({ page }) => {
    await page.goto('/admin.html');

    const bar = page.locator('#signalr-warning');

    // Initially hidden
    await expect(bar).not.toBeVisible();

    // Simulate disconnect
    await page.evaluate(() => {
      if (typeof window.setSignalRIndicatorState === 'function') {
        window.setSignalRIndicatorState(false);
      }
    });

    await expect(bar).toBeVisible();
    await expect(bar).toContainText('Reconnecting to server');

    // Simulate reconnect
    await page.evaluate(() => {
      if (typeof window.setSignalRIndicatorState === 'function') {
        window.setSignalRIndicatorState(true);
      }
    });

    await expect(bar).not.toBeVisible();

    // Ensure old inline indicator is removed
    const oldIndicator = page.locator('[data-testid="signalr-status"]');
    await expect(oldIndicator).toHaveCount(0);
  });
});
