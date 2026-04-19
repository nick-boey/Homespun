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
 * - Local: launch the Aspire AppHost (`dotnet run --project ../Homespun.AppHost
 *   --launch-profile dev-mock`) and then `E2E_BASE_URL=http://localhost:5173 npm
 *   run test:e2e`, or let this config drive a lightweight non-Aspire stack below.
 * - CI: `webServer` entries below start the .NET mock server and Vite directly,
 *   bypassing Aspire to avoid DCP cert-trust + container-pull overhead on fresh
 *   runners. Aspire remains the primary dev-orchestration surface for
 *   local inner-loop (dev-mock / dev-live / dev-windows / dev-container).
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

  // Start the .NET mock server and Vite dev server directly. This mirrors the
  // pre-Aspire setup and keeps CI boot predictable — Aspire/DCP cert trust
  // and PLG image pulls were pushing cold runs past Playwright's webServer
  // timeout. Vite's dev proxy (vite.config.ts) forwards /api to 5101.
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : [
        {
          command: 'cd ../Homespun.Server && dotnet run --launch-profile mock',
          url: 'http://localhost:5101/health',
          reuseExistingServer: !process.env.CI,
          timeout: 120000,
          stdout: 'pipe',
          stderr: 'pipe',
        },
        {
          command: 'npm run dev',
          url: 'http://localhost:5173',
          reuseExistingServer: !process.env.CI,
          timeout: 30000,
          stdout: 'pipe',
          stderr: 'pipe',
        },
      ],
})
