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

    // Wait for the bar to become visible; retry once for flaky engines (WebKit)
    try {
      await expect(bar).toBeVisible({ timeout: 10000 });
    } catch (err) {
      // Retry invoking the indicator and wait a bit longer
      await page.evaluate(() => {
        if (typeof window.setSignalRIndicatorState === 'function') {
          window.setSignalRIndicatorState(false);
        }
      });
      await expect(bar).toBeVisible({ timeout: 5000 });
    }

    await expect(bar).toContainText('Reconnecting to server');

    // Simulate reconnect
    await page.evaluate(() => {
      if (typeof window.setSignalRIndicatorState === 'function') {
        window.setSignalRIndicatorState(true);
      }
    });

    // Wait for the bar to hide; allow extra time for animations or slow engines
    await expect(bar).not.toBeVisible({ timeout: 10000 });

    // Ensure old inline indicator is removed
    const oldIndicator = page.locator('[data-testid="signalr-status"]');
    await expect(oldIndicator).toHaveCount(0);
  });
});
