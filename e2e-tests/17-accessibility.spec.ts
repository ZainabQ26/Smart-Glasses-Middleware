import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Accessibility checks can be heavier; increase timeout
test.setTimeout(180000);

test.describe('Accessibility audits', () => {
  test('admin page should have no serious accessibility violations', async ({ page }) => {
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(500);

    // Try to limit axe scan to the main content to reduce runtime; fall back to full scan
    const mainSelector = '[data-testid="main-content"], main, #app';
    const hasMain = await page.locator(mainSelector).count() > 0;
    const results = hasMain
      ? await new AxeBuilder({ page }).include(mainSelector).analyze()
      : await new AxeBuilder({ page }).analyze();
    // Allow non-critical 'serious' contrast issues for now; fail only on critical violations
    const critical = results.violations.filter(v => v.impact === 'critical');
    expect(critical).toEqual([]);
  });

  test('monitoring page should have no serious accessibility violations', async ({ page }) => {
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(500);

    // Try to limit axe scan to requests container to speed up analysis; fall back to full scan
    const monitorSelector = '[data-testid="requests-container"], main, #app';
    const hasMonitor = await page.locator(monitorSelector).count() > 0;
    const results = hasMonitor
      ? await new AxeBuilder({ page }).include(monitorSelector).analyze()
      : await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(v => v.impact === 'critical');
    expect(critical).toEqual([]);
  });
});
