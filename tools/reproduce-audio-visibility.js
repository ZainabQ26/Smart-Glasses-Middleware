const { chromium } = require('playwright');
(async()=>{
  const browser = await chromium.launch({ headless: false });
  const page = await browser.newPage();
  page.on('console', msg => console.log('[BROWSER]', msg.type(), msg.text()));
  page.on('pageerror', err => console.log('[PAGEERROR]', err.message));
  page.on('requestfailed', req => console.log('[REQFAILED]', req.url(), req.failure() && req.failure().errorText));

  await page.goto('http://localhost:5000/admin.html');
  console.log('navigated');
  await page.waitForTimeout(500);
  console.log('clicking prompts tab');
  await page.click('button[data-tab="prompts"]');
  await page.waitForTimeout(500);
  console.log('clicking attach');
  await page.click('#prompt-show-file-btn');
  await page.waitForTimeout(1000);
  const visible = await page.isVisible('#prompt-file-input');
  console.log('prompt-file-input visible:', visible);
  const computed = await page.evaluate(() => {
    const el = document.getElementById('prompt-file-input');
    if (!el) return null;
    return {
      display: window.getComputedStyle(el).display,
      visibility: window.getComputedStyle(el).visibility,
      offsetWidth: el.offsetWidth,
      offsetHeight: el.offsetHeight,
      parentId: el.parentElement ? el.parentElement.id : null,
      parentDisplay: el.parentElement ? window.getComputedStyle(el.parentElement).display : null,
      closestTabContent: (el.closest('.tab-content') && el.closest('.tab-content').id) || null,
      closestTabDisplay: el.closest('.tab-content') ? window.getComputedStyle(el.closest('.tab-content')).display : null
    };
  });
  console.log('computed:', computed);
  await page.screenshot({ path: 'tools/debug-prompt.png' });
  console.log('screenshot saved to tools/debug-prompt.png');
  await page.waitForTimeout(2000);
  await browser.close();
})();
