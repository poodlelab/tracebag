import { expect, test } from '@playwright/test';

const adminUser = process.env.TRACEBAG_BROWSER_ADMIN_USER ?? 'admin';
const adminPassword = process.env.TRACEBAG_BROWSER_ADMIN_PASSWORD ?? '';

async function login(page: import('@playwright/test').Page): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Username').fill(adminUser);
  await page.getByLabel('Password').fill(adminPassword);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page.getByRole('heading', { name: 'Containers' })).toBeVisible();
}

test('operator can inspect logs, run counters, capture an artifact, and log out', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await login(page);

  const demoCard = page.locator('.container-card:has(.status-dot.ok)').filter({ hasText: 'Tracebag Demo API' });
  await expect(demoCard).toBeVisible();
  await demoCard.getByRole('link', { name: 'Logs' }).click();
  await expect(page.getByText('searchable logs')).toBeVisible();
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await expect(page.locator('.log-row').first()).toBeVisible();

  await page.getByRole('link', { name: 'Metrics', exact: true }).click();
  await page.getByRole('button', { name: 'Discover' }).click();
  const processSelect = page.getByLabel('process');
  await expect(processSelect.locator('option')).toHaveCount(2);
  await processSelect.selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.locator('.counter-table tbody tr').first()).toBeVisible({ timeout: 45_000 });
  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeEnabled();

  await page.getByRole('link', { name: 'Diagnostics', exact: true }).click();
  await expect(page.getByText('new capture')).toBeVisible();
  await page.getByRole('button', { name: /Start Stack snapshot/i }).click();
  const completedJob = page.locator('.diagnostic-job').first();
  await expect(completedJob.locator('.status-pill')).toHaveText('completed', { timeout: 60_000 });
  const downloadPromise = page.waitForEvent('download');
  await completedJob.getByRole('link', { name: 'Download' }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).not.toBe('');

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible();
  await expect(page).toHaveURL(/\/login$/);
});
