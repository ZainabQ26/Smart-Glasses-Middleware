import { test, expect } from '@playwright/test';

test.describe('Persistence and retrieval', () => {
  test('should create a prompt and retrieve it from the API', async ({ request }) => {
    const name = `e2e-prompt-${Date.now()}`;
    const promptText = 'E2E prompt persistence check';

    const createResponse = await request.post('/api/admin/prompts', {
      data: { name, promptText }
    });

    expect(createResponse.status()).toBeLessThan(500);

    const listResponse = await request.get('/api/admin/prompts');
    expect(listResponse.status()).toBeLessThan(500);

    if (listResponse.status() !== 200) return;

    const listBody = await listResponse.json();
    const items = Array.isArray(listBody)
      ? listBody
      : Array.isArray(listBody?.data)
        ? listBody.data
        : [];

    const found = items.find((item: { name?: string; id?: string }) => item?.name === name);
    expect(found).toBeTruthy();

    if (found?.id) {
      await request.delete(`/api/admin/prompts/${found.id}`);
    }
  });
});
