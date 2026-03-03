import { defineConfig, devices } from '@playwright/test'

/**
 * E2E test configuration for Homespun React frontend.
 * Tests run against the full application stack (React frontend + ASP.NET backend).
 *
 * Configuration:
 * - E2E_BASE_URL: Override the base URL (default: http://localhost:5101)
 * - CI: Set to 'true' in CI environments for optimized settings
 *
 * Usage:
 * - Local: Start the mock server with `./scripts/mock.sh`, then run `npm run test:e2e`
 * - CI: Tests automatically start the server via webServer config
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
    baseURL: process.env.E2E_BASE_URL || 'http://localhost:5101',
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

  // In CI or when E2E_BASE_URL is not set, start the server automatically
  // This mirrors the behavior of HomespunFixture in the C# E2E tests
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: 'cd ../Homespun.Server && dotnet run --launch-profile mock',
        url: 'http://localhost:5101/health',
        reuseExistingServer: !process.env.CI,
        timeout: 120000,
        stdout: 'pipe',
        stderr: 'pipe',
      },
})
