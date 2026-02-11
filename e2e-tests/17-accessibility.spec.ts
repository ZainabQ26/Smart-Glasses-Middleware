import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.describe('Accessibility audits', () => {
  test('admin page should have no serious accessibility violations', async ({ page }) => {
    await page.goto('/admin.html', { waitUntil: 'domcontentloaded' });

    const results = await new AxeBuilder({ page }).analyze();
    const serious = results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
    expect(serious).toEqual([]);
  });

  test('monitoring page should have no serious accessibility violations', async ({ page }) => {
    await page.goto('/monitoring.html', { waitUntil: 'domcontentloaded' });

    const results = await new AxeBuilder({ page }).analyze();
    const serious = results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
    expect(serious).toEqual([]);
  });
});
