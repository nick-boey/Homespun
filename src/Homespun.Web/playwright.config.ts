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
 * - Local: Start the mock server with `./scripts/mock.sh`, then run `npm run test:e2e`
 * - CI: Tests automatically start both Vite dev server and .NET backend
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

  // Start both the .NET backend and Vite dev server
  // Vite dev server proxies API calls to the .NET backend (configured in vite.config.ts)
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : [
        {
          // Start the .NET backend first
          command: 'cd ../Homespun.Server && dotnet run --launch-profile mock',
          url: 'http://localhost:5101/health',
          reuseExistingServer: !process.env.CI,
          timeout: 120000,
          stdout: 'pipe',
          stderr: 'pipe',
        },
        {
          // Then start Vite dev server which proxies to the backend
          command: 'npm run dev',
          url: 'http://localhost:5173',
          reuseExistingServer: !process.env.CI,
          timeout: 30000,
          stdout: 'pipe',
          stderr: 'pipe',
        },
      ],
})
