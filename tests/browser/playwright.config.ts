import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [['line'], ['html', { open: 'never' }]] : 'line',
  timeout: 120_000,
  expect: { timeout: 30_000 },
  outputDir: 'test-results',
  use: {
    baseURL: process.env.TRACEBAG_BROWSER_BASE_URL,
    browserName: 'chromium',
    ignoreHTTPSErrors: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    launchOptions: {
      args: ['--host-resolver-rules=MAP tracebag.test 127.0.0.1']
    }
  }
});
