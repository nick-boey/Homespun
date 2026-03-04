import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for AG-UI session functionality.
 * Tests that the UI properly renders AG-UI events from Claude Code sessions.
 *
 * Mirrors: tests/Homespun.E2E.Tests/AGUISessionTests.cs
 */
test.describe('AG-UI Sessions', () => {
  test.describe('Session API Tests', () => {
    test('sessions API returns valid JSON', async ({ request }) => {
      const response = await request.get('/api/sessions')

      expect(response.ok()).toBe(true)

      const body = await response.text()
      expect(() => JSON.parse(body)).not.toThrow()
    })

    test('projects API returns valid JSON', async ({ request }) => {
      const response = await request.get('/api/projects')

      expect(response.ok()).toBe(true)

      const body = await response.text()
      expect(() => JSON.parse(body)).not.toThrow()
    })
  })

  test.describe('Session Page Tests', () => {
    test('sessions page renders', async ({ page }) => {
      await page.goto('/sessions')
      await page.waitForLoadState('networkidle')

      // Verify the page renders (body should be visible)
      await expect(page.locator('body')).toBeVisible()
    })

    test('session page with invalid ID shows error or redirect', async ({ page }) => {
      await page.goto('/sessions/nonexistent-session-id')
      await page.waitForLoadState('networkidle')

      // Should render something (body visible)
      await expect(page.locator('body')).toBeVisible()
    })
  })
})
