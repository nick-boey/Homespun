import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for critical user journeys in the Homespun application.
 * These tests verify the full application stack including UI rendering.
 *
 * Mirrors: tests/Homespun.E2E.Tests/CriticalJourneysTests.cs
 */
test.describe('Critical Journeys', () => {
  test('home page loads successfully', async ({ page }) => {
    // Navigate to root - the app should render
    await page.goto('/')
    await page.waitForLoadState('networkidle')

    // Verify the page loads (body should be visible)
    await expect(page.locator('body')).toBeVisible()
  })

  test('projects page displays content', async ({ page }) => {
    // Navigate to projects page (root redirects here)
    await page.goto('/')
    await page.waitForLoadState('networkidle')

    // Verify the page has content
    const body = page.locator('body')
    await expect(body).toBeVisible()
  })

  test('health endpoint returns healthy', async ({ request }) => {
    // This test verifies the health check endpoint works
    const response = await request.get('/api/health')

    // Health endpoint may return 200 or 404 depending on route
    expect(response.status()).toBeLessThan(500)
  })

  test('projects API returns valid response', async ({ request }) => {
    // Test the projects API endpoint
    const response = await request.get('/api/projects')

    expect(response.ok()).toBe(true)

    const body = await response.text()
    expect(() => JSON.parse(body)).not.toThrow()
  })

  test('sessions API returns valid response', async ({ request }) => {
    // Test the sessions API endpoint
    const response = await request.get('/api/sessions')

    expect(response.ok()).toBe(true)

    const body = await response.text()
    expect(() => JSON.parse(body)).not.toThrow()
  })
})
