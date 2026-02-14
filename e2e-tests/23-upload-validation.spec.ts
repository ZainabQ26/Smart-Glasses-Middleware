import { test, expect } from '@playwright/test';

test.describe('Upload validation', () => {
  test('should reject audio uploads to /api/admin/files', async ({ request }) => {
    const audioBuffer = Buffer.from([0, 1, 2, 3]);

    const response = await request.post('/api/admin/files', {
      multipart: {
        file: {
          name: 'test.wav',
          mimeType: 'audio/wav',
          buffer: audioBuffer
        },
        label: 'test-audio'
      }
    });

    expect(response.status()).toBe(400);
    const body = await response.json();
    // Ensure the response indicates audio is not allowed
    expect(body.detail || body.title || JSON.stringify(body)).toContain('Audio');
  });

  test('should accept prompt audio via /api/ask (promptAudio)', async ({ request }) => {
    const audioBuffer = Buffer.from([0, 1, 2, 3]);

    const response = await request.post('/api/ask', {
      multipart: {
        promptAudio: {
          name: 'prompt.wav',
          mimeType: 'audio/wav',
          buffer: audioBuffer
        },
        // Provide an input transcription so the endpoint can proceed even if
        // the transcription service isn't available in the test environment.
        input: 'simulated transcription text'
      }
    });

    // /api/ask processes media and should accept the prompt audio (202 Accepted or 200 OK depending on impl)
    expect(response.status()).toBeGreaterThanOrEqual(200);
    expect(response.status()).toBeLessThan(300);
  });
});
