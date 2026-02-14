(async () => {
  const { chromium } = require('playwright');
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto('http://localhost:5000/admin.html');

  const promptsTab = await page.$('[data-testid="tab-prompts"]');
  // Click to activate the Prompts tab, then hover to check active+hover behavior
  await promptsTab.click();
  await page.waitForTimeout(150);
  await promptsTab.hover();
  // give transition a moment
  await page.waitForTimeout(350);
  const isHoverLight = await page.evaluate(el => el.matches(':hover'), promptsTab);
  const colorLight = await page.evaluate(el => getComputedStyle(el).color, promptsTab);
  console.log('Light matches :hover?:', isHoverLight);
  console.log('Light hover color:', colorLight);

  // Toggle dark mode and retry
  await page.click('#theme-toggle');
  await page.waitForTimeout(300);
  await promptsTab.hover();
  await page.waitForTimeout(350);
  const isHoverDark = await page.evaluate(el => el.matches(':hover'), promptsTab);
  const colorDark = await page.evaluate(el => getComputedStyle(el).color, promptsTab);
  console.log('Dark matches :hover?:', isHoverDark);
  console.log('Dark hover color:', colorDark);

  await browser.close();
})();
