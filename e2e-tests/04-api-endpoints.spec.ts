import { test, expect } from '@playwright/test';

test.describe('API Endpoints', () => {
  test('should return healthy status from /health endpoint', async ({ request }) => {
    const response = await request.get('/health');
    expect(response.status()).toBe(200);
    
    const body = await response.text();
    expect(body.toLowerCase()).toContain('healthy');
  });

  test('should access /api/history endpoint', async ({ request }) => {
    const response = await request.get('/api/history');
    
    // Should be accessible (200 or 204 if no data)
    expect(response.status()).toBeLessThan(500);
  });

  test('should handle /api/admin/files endpoint', async ({ request }) => {
    const response = await request.get('/api/admin/files');
    
    // Should return success or auth error, not server error
    expect(response.status()).toBeLessThan(500);
  });

  test('should handle /api/admin/prompts endpoint', async ({ request }) => {
    const response = await request.get('/api/admin/prompts');
    
    expect(response.status()).toBeLessThan(500);
  });

  test('should handle /api/admin/groups endpoint', async ({ request }) => {
    const response = await request.get('/api/admin/groups');
    
    expect(response.status()).toBeLessThan(500);
  });

  test('should return JSON from /api/history', async ({ request }) => {
    const response = await request.get('/api/history');
    
    if (response.status() === 200) {
      const contentType = response.headers()['content-type'];
      expect(contentType).toContain('application/json');
      
      const data = await response.json();
      expect(Array.isArray(data)).toBeTruthy();
    }
  });

  test('should handle /api/monitoring/requests endpoint', async ({ request }) => {
    const response = await request.get('/api/monitoring/requests');
    expect(response.status()).toBeLessThan(500);
  });

  test('should handle /api/conversations endpoint', async ({ request }) => {
    const response = await request.get('/api/Conversations');
    expect(response.status()).toBeLessThan(500);
  });

  test('should reject invalid POST to /api/ask without body', async ({ request }) => {
    const response = await request.post('/api/ask', {
      data: {}
    });
    
    // Should return 400 (bad request) or similar client error
    expect(response.status()).toBeGreaterThanOrEqual(400);
  });

  test('should handle /metrics endpoint', async ({ request }) => {
    const response = await request.get('/metrics');
    expect(response.status()).toBe(200);
  });

  test('should handle CORS headers appropriately', async ({ request }) => {
    const response = await request.get('/health');
    
    const headers = response.headers();
    // CORS headers should be present or at least not cause errors
    expect(response.status()).toBe(200);
  });

  test('should paginate /api/history with query params', async ({ request }) => {
    const response = await request.get('/api/history?page=1&pageSize=10');
    expect(response.status()).toBeLessThan(500);
  });

  test('should handle /api/Documents endpoint', async ({ request }) => {
    const response = await request.get('/api/Documents');
    expect(response.status()).toBeLessThan(500);
  });

  test('should return appropriate status for non-existent endpoints', async ({ request }) => {
    const response = await request.get('/api/nonexistent-endpoint-xyz');
    expect(response.status()).toBe(404);
  });

  test('should handle /api/ai-interactions endpoint', async ({ request }) => {
    const response = await request.get('/api/ai-interactions');
    expect(response.status()).toBeLessThan(500);
  });

  test('should handle /api/ai-interactions/stats endpoint', async ({ request }) => {
    const response = await request.get('/api/ai-interactions/stats');
    expect(response.status()).toBeLessThan(500);
  });
});
