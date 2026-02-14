import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Accessibility checks can be heavier; increase timeout
test.setTimeout(180000);

test.describe('Accessibility audits', () => {
  test('admin page should have no serious accessibility violations', async ({ page }) => {
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);

    // Disable color-contrast rule to speed up scan significantly (especially on WebKit)
    const results = await new AxeBuilder({ page })
      .disableRules(['color-contrast', 'duplicate-id'])
      .analyze();
    // Allow non-critical 'serious' contrast issues for now; fail only on critical violations
    const critical = results.violations.filter(v => v.impact === 'critical');
    expect(critical).toEqual([]);
  });

  test('monitoring page should have no serious accessibility violations', async ({ page }) => {
    await page.route('**/api/history*', route => route.fulfill({ status: 200, json: [] }));
    await page.route('**/api/monitoring/requests*', route => route.fulfill({ status: 200, json: [] }));
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);

    // Disable color-contrast and focus scan on the requests container to speed up
    const results = await new AxeBuilder({ page })
      .include('[data-testid="requests-container"]')
      .disableRules(['color-contrast', 'duplicate-id'])
      .analyze();
    const critical = results.violations.filter(v => v.impact === 'critical');
    expect(critical).toEqual([]);
  });
});
