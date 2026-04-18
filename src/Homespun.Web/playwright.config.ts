import { defineConfig, devices } from '@playwright/test'

/**
 * E2E test configuration for Homespun React frontend.
 * Tests run against the full application stack (React frontend + ASP.NET backend).
 *
 * Configuration:
 * - E2E_BASE_URL: Override the base URL (default: http://localhost:5173 - Vite dev server)
 * - CI: Set to 'true' in CI environments for optimized settings
 *
 * Usage:
 * - Local: Launch the Aspire AppHost via
 *   `dotnet run --project ../Homespun.AppHost --launch-profile dev-mock`, then
 *   run `npm run test:e2e`
 * - CI: `webServer` below drives the AppHost automatically
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI
    ? [['github'], ['html', { open: 'never' }]]
    : [['html', { open: 'never' }]],
  timeout: 30000,
  expect: {
    timeout: 10000,
  },

  use: {
    // Tests run against Vite dev server which proxies API calls to .NET backend
    baseURL: process.env.E2E_BASE_URL || 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    ignoreHTTPSErrors: true,
    viewport: { width: 1920, height: 1080 },
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    // Uncomment for additional browser coverage:
    // {
    //   name: 'firefox',
    //   use: { ...devices['Desktop Firefox'] },
    // },
    // {
    //   name: 'webkit',
    //   use: { ...devices['Desktop Safari'] },
    // },
  ],

  // Launch the full dev stack through the Aspire AppHost (server + Vite + PLG).
  // Wait on the Vite dev-server port — it starts last in the AppHost WaitFor
  // chain (loki → server → web), so a ready signal there guarantees the whole
  // stack is up. Server lives at :5101 via Vite's /api proxy.
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: 'dotnet run --project ../Homespun.AppHost --launch-profile dev-mock',
        url: 'http://localhost:5173/',
        reuseExistingServer: !process.env.CI,
        timeout: 180000,
        stdout: 'pipe',
        stderr: 'pipe',
      },
})
