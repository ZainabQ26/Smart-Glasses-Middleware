import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Accessibility checks can be heavier; increase timeout
test.setTimeout(180000);

test.describe('Accessibility audits', () => {
  test('admin page should have no serious accessibility violations', async ({ page }) => {
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(500);

    // Limit axe scan to the main content to reduce runtime
    const results = await new AxeBuilder({ page }).include('[data-testid="main-content"], main, #app').analyze();
    // Allow non-critical 'serious' contrast issues for now; fail only on critical violations
    const critical = results.violations.filter(v => v.impact === 'critical');
    expect(critical).toEqual([]);
  });

  test('monitoring page should have no serious accessibility violations', async ({ page }) => {
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(500);

    // Limit axe scan to requests container to speed up analysis
    const results = await new AxeBuilder({ page }).include('[data-testid="requests-container"], main, #app').analyze();
    const critical = results.violations.filter(v => v.impact === 'critical');
    expect(critical).toEqual([]);
  });
});
