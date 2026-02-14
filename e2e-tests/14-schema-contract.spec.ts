import { test, expect } from '@playwright/test';
import Ajv from 'ajv';

const ajv = new Ajv({ allErrors: true, strict: false });

const requestHistorySchema = {
  type: 'object',
  required: ['id', 'timestamp', 'requestType'],
  properties: {
    id: { type: 'string' },
    timestamp: { type: 'string' },
    requestType: { type: 'string' },
    inputText: { type: 'string' },
    detectedTask: { type: ['string', 'null'] },
    response: { type: ['string', 'null'] },
    usedAdvancedModel: { type: 'boolean' },
    mediaStorageKey: { type: ['string', 'null'] }
  },
  additionalProperties: true
};

const monitoringRequestSchema = {
  type: 'object',
  required: ['requestId', 'status', 'startTime'],
  properties: {
    requestId: { type: 'string' },
    status: { type: 'string' },
    startTime: { type: 'string' },
    endTime: { type: ['string', 'null'] },
    requestType: { type: 'string' },
    description: { type: ['string', 'null'] },
    progress: { type: ['number', 'null'] }
  },
  additionalProperties: true
};

test.describe('Schema and contract validation', () => {
  test('should validate /api/history response schema', async ({ request }) => {
    const response = await request.get('/api/history');
    expect(response.status()).toBeLessThan(500);

    if (response.status() !== 200) return;

    const data = await response.json();
    expect(Array.isArray(data)).toBeTruthy();

    if (data.length === 0) return;

    const validate = ajv.compile(requestHistorySchema);
    for (const entry of data) {
      const ok = validate(entry);
      expect(ok).toBeTruthy();
    }
  });

  test('should validate /api/monitoring/requests response schema', async ({ request }) => {
    const response = await request.get('/api/monitoring/requests');
    expect(response.status()).toBeLessThan(500);

    if (response.status() !== 200) return;

    const data = await response.json();
    expect(Array.isArray(data)).toBeTruthy();

    if (data.length === 0) return;

    const validate = ajv.compile(monitoringRequestSchema);
    for (const entry of data) {
      const ok = validate(entry);
      expect(ok).toBeTruthy();
    }
  });
});
