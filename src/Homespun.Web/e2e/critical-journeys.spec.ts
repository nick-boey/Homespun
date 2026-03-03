import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for critical user journeys in the Homespun application.
 * These tests verify the full application stack including UI rendering.
 *
 * Mirrors: tests/Homespun.E2E.Tests/CriticalJourneysTests.cs
 */
test.describe('Critical Journeys', () => {
  test('home page loads successfully', async ({ page }) => {
    // Navigate to projects page (the app redirects root to projects)
    await page.goto('/projects')
    await page.waitForLoadState('networkidle')

    // Verify the page title contains relevant text
    await expect(page).toHaveTitle(/Projects|Homespun/)
  })

  test('projects page displays projects list', async ({ page }) => {
    // Navigate to projects page
    await page.goto('/projects')
    await page.waitForLoadState('networkidle')

    // Verify that the page contains project-related content
    // The page should show either a list of projects or a "no projects" message
    const mainContent = page.locator('main')
    await expect(mainContent).toBeVisible()
  })

  test('navigation works between pages', async ({ page }) => {
    // Start at home page
    await page.goto('/')
    await page.waitForLoadState('networkidle')

    // Navigate to settings page via nav link
    const settingsLink = page.locator('a[href*="settings"], a:has-text("Settings")').first()
    await settingsLink.click()
    await page.waitForLoadState('networkidle')

    // Verify URL changed to settings
    expect(page.url()).toContain('settings')
  })

  test('settings page loads successfully', async ({ page }) => {
    // Navigate to settings page
    await page.goto('/settings')
    await page.waitForLoadState('networkidle')

    // Verify the settings page loads
    const mainContent = page.locator('main')
    await expect(mainContent).toBeVisible()
  })

  test('health endpoint returns healthy', async ({ request }) => {
    // This test verifies the health check endpoint works
    const response = await request.get('/health')

    expect(response.ok()).toBe(true)
  })

  test('swagger page loads successfully', async ({ page }) => {
    // Navigate to Swagger UI
    await page.goto('/swagger')
    await page.waitForLoadState('networkidle')

    // Verify Swagger UI loads - use specific selector
    await expect(page.locator('div.swagger-ui')).toBeVisible({ timeout: 10000 })
  })
})
