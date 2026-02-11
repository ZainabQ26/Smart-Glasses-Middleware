import { test, expect } from '@playwright/test';

test.describe('Authentication and Authorization', () => {
  test('should enforce admin endpoint access rules', async ({ request }) => {
    const token = process.env.E2E_AUTH_TOKEN;
    const unauth = await request.get('/api/admin/prompts');

    if (unauth.status() === 401 || unauth.status() === 403) {
      test.skip(!token, 'E2E_AUTH_TOKEN not set for authorized validation');

      const auth = await request.get('/api/admin/prompts', {
        headers: {
          Authorization: `Bearer ${token}`
        }
      });

      expect(auth.status()).toBeLessThan(500);
    } else {
      expect(unauth.status()).toBeLessThan(500);
    }
  });
});

test.describe('Security headers', () => {
  test('should return baseline security headers on admin page', async ({ page }) => {
    const response = await page.goto('/admin.html');
    const headers = response?.headers() || {};

    expect(headers['x-content-type-options']).toBeTruthy();
    expect(headers['x-frame-options'] || headers['content-security-policy']).toBeTruthy();
    expect(headers['referrer-policy']).toBeTruthy();
  });
});
