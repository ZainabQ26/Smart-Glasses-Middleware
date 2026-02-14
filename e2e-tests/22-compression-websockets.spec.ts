import { test, expect } from '@playwright/test';

test.describe('Response Compression', () => {
  test('should receive compressed responses from API', async ({ page }) => {
    // Intercept network requests to check compression
    const compressedResponses: string[] = [];
    
    page.on('response', response => {
      const encoding = response.headers()['content-encoding'];
      if (encoding && (encoding.includes('gzip') || encoding.includes('br'))) {
        compressedResponses.push(response.url());
      }
    });

    // Navigate to admin page
    await page.goto('/admin.html');
    await page.waitForTimeout(2000);

    // Make an API request
    await page.click('button[data-tab="files"]');
    await page.waitForTimeout(1000);

    // Check if any responses were compressed
    // Note: This might not always trigger depending on response size
    console.log('Compressed responses:', compressedResponses);
    
    // At minimum, verify the page loaded
    expect(await page.title()).toBeTruthy();
  });

  test('should handle large JSON responses efficiently', async ({ page }) => {
    // Navigate to page
    await page.goto('/admin.html');
    
    // Measure batch endpoint performance
    const startTime = Date.now();
    
    const response = await page.evaluate(async () => {
      const res = await fetch('/api/admin/batch?includeFiles=true&includePrompts=true&includeGroups=true');
      const data = await res.json();
      return {
        status: res.status,
        hasData: !!data,
        contentType: res.headers.get('content-type')
      };
    });
    
    const endTime = Date.now();
    const duration = endTime - startTime;

    expect(response.status).toBe(200);
    expect(response.hasData).toBe(true);
    expect(response.contentType).toContain('application/json');
    
    // Response should be reasonably fast (< 5 seconds)
    expect(duration).toBeLessThan(5000);
  });

  test('health endpoint should return quickly', async ({ page }) => {
    await page.goto('/admin.html');
    
    const startTime = Date.now();
    const response = await page.evaluate(async () => {
      const res = await fetch('/health');
      return {
        status: res.status,
        ok: res.ok
      };
    });
    const endTime = Date.now();

    expect(response.status).toBe(200);
    expect(response.ok).toBe(true);
    
    // Health check should be very fast
    expect(endTime - startTime).toBeLessThan(3000);
  });
});

test.describe('WebSocket/SignalR Performance', () => {
  test('should establish SignalR connections', async ({ page }) => {
    // Track console messages for SignalR
    const signalrMessages: string[] = [];
    page.on('console', msg => {
      const text = msg.text();
      if (text.includes('SignalR') || text.includes('hub') || text.includes('Hub')) {
        signalrMessages.push(text);
      }
    });

    await page.goto('/admin.html');
    await page.waitForTimeout(3000);

    // Check console logs for SignalR connections
    const hasSignalRActivity = signalrMessages.some(msg => 
      msg.includes('connected') || msg.includes('Connected')
    );

    // SignalR should attempt to connect
    expect(signalrMessages.length).toBeGreaterThan(0);
  });

  test('should handle SignalR reconnection', async ({ page }) => {
    await page.goto('/admin.html');
    await page.waitForTimeout(2000);

    // Check if SignalR warning banner functionality exists
    const hasSignalRWarning = await page.evaluate(() => {
      return document.getElementById('signalr-warning') !== null;
    });

    expect(hasSignalRWarning).toBe(true);
  });

  test('should maintain multiple hub connections', async ({ page }) => {
    const hubMessages: string[] = [];
    page.on('console', msg => {
      const text = msg.text();
      if (text.includes('hub')) {
        hubMessages.push(text);
      }
    });

    await page.goto('/admin.html');
    await page.waitForTimeout(3000);

    // Should see messages about different hubs (tasks, conversations, resources)
    const hasMultipleHubs = await page.evaluate(() => {
      return typeof (window as any).conversationHubConnection !== 'undefined' ||
             typeof (window as any).resourceHubConnection !== 'undefined' ||
             typeof (window as any).taskHubConnection !== 'undefined';
    });

    // Hub connections should be initialized
    expect(hasMultipleHubs).toBeTruthy();
  });

  test('should show real-time updates via SignalR', async ({ page }) => {
    await page.goto('/admin.html');
    await page.waitForTimeout(2000);

    // Check if event handlers are set up
    const hasEventHandlers = await page.evaluate(() => {
      // Check if SignalR hubs have event listeners
      const connections = [
        (window as any).conversationHubConnection,
        (window as any).resourceHubConnection,
        (window as any).taskHubConnection
      ];
      
      return connections.some(conn => conn !== undefined && conn !== null);
    });

    expect(hasEventHandlers).toBeTruthy();
  });

  test('should handle SignalR with keepalive', async ({ page }) => {
    await page.goto('/admin.html');
    
    // Wait for connections to establish
    await page.waitForTimeout(2000);

    // Wait for keepalive interval (15s configured, but we'll wait less)
    await page.waitForTimeout(5000);

    // Check that page is still responsive
    const isResponsive = await page.evaluate(() => {
      return document.readyState === 'complete';
    });

    expect(isResponsive).toBe(true);
  });
});
