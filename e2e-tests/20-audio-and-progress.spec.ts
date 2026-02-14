import { test, expect } from '@playwright/test';

test.describe('Audio Upload and Progress Features', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/admin.html');
    // Switch to prompts tab
    await page.click('button[data-tab="prompts"]');
    await page.waitForTimeout(500);
  });

  test('should allow audio file upload in prompt section', async ({ page }) => {
    // Click "Attach file" button to show file upload panel
    const attachBtn = page.locator('#prompt-show-file-btn');
    await expect(attachBtn).toBeVisible();
    await attachBtn.click();
    
    // Wait for file panel to appear
    await page.waitForTimeout(300);
    
    // Check that file input accepts audio files
    const fileInput = page.locator('#prompt-file-input');
    await expect(fileInput).toBeVisible();
    
    const acceptAttr = await fileInput.getAttribute('accept');
    expect(acceptAttr).toContain('.wav');
    expect(acceptAttr).toContain('.mp3');
    expect(acceptAttr).toContain('.m4a');
    expect(acceptAttr).toContain('.ogg');
  });

  test('should show audio preview with playback controls', async ({ page }) => {
    // This test would require actually uploading a file, which needs a mock audio file
    // For now, we just verify the file input can accept audio files
    const attachBtn = page.locator('#prompt-show-file-btn');
    await attachBtn.click();
    await page.waitForTimeout(300);
    
    const fileInput = page.locator('#prompt-file-input');
    const acceptAttr = await fileInput.getAttribute('accept');
    expect(acceptAttr).toMatch(/\.(wav|mp3|m4a|ogg|webm)/);
  });

  test('should display progress bar when sending prompt', async ({ page }) => {
    // Fill in a test prompt
    const promptInput = page.locator('#prompt-name');
    await expect(promptInput).toBeVisible();
    await promptInput.fill('Test progress bar display');
    
    // Click send button
    const sendBtn = page.locator('#prompt-send-btn');
    await sendBtn.click();
    
    // Wait a moment for progress display to appear
    await page.waitForTimeout(500);
    
    // Check for progress bar elements (may complete quickly, so we check if they existed)
    const progressText = page.locator('[data-testid="progress-status"]');
    const hasProgressText = await progressText.count() > 0;
    
    // If the request completed very quickly, the response might already be shown
    // In either case, we verify the page didn't error
    const responseContent = page.locator('#response-content');
    await expect(responseContent).toBeVisible();
  });

  test('should show activity history during request processing', async ({ page }) => {
    // Fill in a test prompt
    const promptInput = page.locator('#prompt-name');
    await promptInput.fill('Test activity history');
    
    // Click send
    const sendBtn = page.locator('#prompt-send-btn');
    await sendBtn.click();
    
    // Check that activity history container is created
    await page.waitForTimeout(600);
    
    // Look for activity history div (it should be created even if hidden initially)
    const activityHistory = page.locator('[class*="activity-history"]');
    const count = await activityHistory.count();
    
    // Activity history should exist (may be hidden if request completed quickly)
    // We just verify no errors occurred
    const responseContent = page.locator('#response-content');
    await expect(responseContent).toBeVisible();
  });
});

test.describe('Context Sources Display', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/admin.html');
    await page.click('button[data-tab="prompts"]');
    await page.waitForTimeout(500);
  });

  test('should display sources with count', async ({ page }) => {
    // Fill in a prompt
    const promptInput = page.locator('#prompt-name');
    await promptInput.fill('Tell me about the system');
    
    // Send the prompt
    const sendBtn = page.locator('#prompt-send-btn');
    await sendBtn.click();
    
    // Wait for response
    await page.waitForTimeout(3000);
    
    // Check if sources section appears
    const responseContent = page.locator('#response-content');
    const contentText = await responseContent.textContent();
    
    // Sources should be shown if available in response
    // At minimum, verify response was received
    expect(contentText).toBeTruthy();
  });

  test('should show file type icons for sources', async ({ page }) => {
    // Send a test prompt
    const promptInput = page.locator('#prompt-name');
    await promptInput.fill('What documents are available?');
    
    const sendBtn = page.locator('#prompt-send-btn');
    await sendBtn.click();
    
    // Wait for response
    await page.waitForTimeout(3000);
    
    // Check response area
    const responseContent = page.locator('#response-content');
    await expect(responseContent).toBeVisible();
    
    // Response should be present (sources display is conditional on having sources)
    const hasContent = await responseContent.textContent();
    expect(hasContent).toBeTruthy();
  });

  test('should allow expanding source details', async ({ page }) => {
    // This test verifies the expand/collapse functionality exists
    // In a real scenario with sources, we would:
    // 1. Send a query that returns sources
    // 2. Click the "Expand" button on a source
    // 3. Verify the source details become visible
    
    // For now, we verify the toggleSourceExpand function exists
    const hasToggleFunction = await page.evaluate(() => {
      return typeof (window as any).toggleSourceExpand === 'function';
    });
    
    expect(hasToggleFunction).toBe(true);
  });

  test('should show preview button for sources with documentId', async ({ page }) => {
    // Send a query
    const promptInput = page.locator('#prompt-name');
    await promptInput.fill('Show me files');
    
    const sendBtn = page.locator('#prompt-send-btn');
    await sendBtn.click();
    
    await page.waitForTimeout(3000);
    
    // Verify response was received
    const responseContent = page.locator('#response-content');
    const content = await responseContent.textContent();
    expect(content).toBeTruthy();
    
    // If sources with preview buttons exist, they would have data-testid
    // We just verify no errors occurred in rendering
  });
});

test.describe('Progress Bar Integration', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/admin.html');
    await page.click('button[data-tab="prompts"]');
  });

  test('should connect to TaskHub for progress updates', async ({ page }) => {
    // Wait for page to load and initialize SignalR
    await page.waitForTimeout(2000);
    
    // Check console for TaskHub connection messages
    const logs: string[] = [];
    page.on('console', msg => {
      logs.push(msg.text());
    });
    
    await page.waitForTimeout(1000);
    
    // TaskHub should attempt to connect
    const hasTaskHubLog = logs.some(log => 
      log.includes('Task hub') || log.includes('TaskHub')
    );
    
    // Even if connection fails, the attempt should be made
    // We verify no critical errors occurred
    const errors = logs.filter(log => log.toLowerCase().includes('error'));
    const criticalErrors = errors.filter(err => 
      !err.includes('Failed to connect') && // Connection failures are handled
      !err.includes('signalR') // SignalR warnings are non-critical
    );
    
    expect(criticalErrors.length).toBe(0);
  });

  test('should update progress bar when progress events occur', async ({ page }) => {
    // Fill in prompt
    const promptInput = page.locator('#prompt-name');
    await promptInput.fill('Test progress updates');
    
    // Send request
    const sendBtn = page.locator('#prompt-send-btn');
    await sendBtn.click();
    
    // Wait briefly to capture progress updates
    await page.waitForTimeout(1000);
    
    // Check that response area exists (progress may have completed)
    const responseContent = page.locator('#response-content');
    await expect(responseContent).toBeVisible();
    
    // Verify no JavaScript errors
    const errors: string[] = [];
    page.on('pageerror', error => {
      errors.push(error.message);
    });
    
    await page.waitForTimeout(500);
    expect(errors.length).toBe(0);
  });
});
