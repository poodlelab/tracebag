import { expect, test } from '@playwright/test';

const adminUser = process.env.TRACEBAG_BROWSER_ADMIN_USER ?? 'admin';
const adminPassword = process.env.TRACEBAG_BROWSER_ADMIN_PASSWORD ?? '';
const websiteUrl = process.env.TRACEBAG_WEBSITE_URL ?? '';

async function expectNoHorizontalOverflow(page: import('@playwright/test').Page): Promise<void> {
  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth
  }));
  expect(dimensions.content).toBeLessThanOrEqual(dimensions.viewport + 1);
}

for (const viewport of [
  { name: 'phone', width: 390, height: 844 },
  { name: 'desktop', width: 1440, height: 1000 }
]) {
  test(`operator navigation fits a ${viewport.name} viewport`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.goto('/login');
    await page.getByLabel('Username').fill(adminUser);
    await page.getByLabel('Password').fill(adminPassword);
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page.getByRole('heading', { name: 'Containers' })).toBeVisible();
    await expect(page.locator('.container-card:has(.status-dot.ok)').filter({ hasText: 'Tracebag Demo API' })).toBeVisible();
    await expectNoHorizontalOverflow(page);
  });

  test(`product page and screenshots fit a ${viewport.name} viewport`, async ({ page }) => {
    test.skip(!websiteUrl, 'TRACEBAG_WEBSITE_URL is required.');
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.goto(websiteUrl);
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Debug a .NET container');
    await expectNoHorizontalOverflow(page);

    const screenshot = page.locator('.screenshot-scroll').first();
    await expect(screenshot).toBeVisible();
    const dimensions = await screenshot.evaluate(element => ({
      renderedWidth: element.getBoundingClientRect().width,
      clientWidth: element.clientWidth,
      scrollWidth: element.scrollWidth
    }));
    expect(dimensions.renderedWidth).toBeLessThanOrEqual(viewport.width);
    if (viewport.name === 'phone') {
      expect(dimensions.scrollWidth).toBeGreaterThan(dimensions.clientWidth);
      await expect(page.locator('.screenshot-scroll-meta').first()).toContainText('Swipe to inspect');
    } else {
      expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth + 1);
    }
  });
}
