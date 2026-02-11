import { test, expect } from '@playwright/test';

test.describe('Health and Basic Endpoints', () => {
  test('should load health endpoint successfully', async ({ page }) => {
    const response = await page.goto('/health');
    expect(response?.status()).toBe(200);
    
    const content = await page.content();
    expect(content).toContain('Healthy');
  });

  test('should load metrics endpoint successfully', async ({ page }) => {
    const response = await page.goto('/metrics');
    expect(response?.status()).toBe(200);
  });

  test('should handle API base URL correctly', async ({ page }) => {
    const response = await page.goto('/api/history');
    expect(response?.status()).toBeLessThan(500);
  });

  test('should reload health page successfully', async ({ page }) => {
    await page.goto('/health');
    await page.reload();
    
    const content = await page.content();
    expect(content).toContain('Healthy');
  });
});
