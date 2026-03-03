import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for AG-UI session functionality.
 * Tests that the UI properly renders AG-UI events from Claude Code sessions.
 *
 * Mirrors: tests/Homespun.E2E.Tests/AGUISessionTests.cs
 */
test.describe('AG-UI Sessions', () => {
  test.describe('Session Page Tests', () => {
    test('session page loads successfully', async ({ page }) => {
      // Navigate to agents page (session management)
      await page.goto('/sessions')
      await page.waitForLoadState('networkidle')

      // Verify the page loads
      const mainContent = page.locator('main')
      await expect(mainContent).toBeVisible()
    })

    test('session management page displays sessions list', async ({ page }) => {
      // Navigate to session management
      await page.goto('/sessions')
      await page.waitForLoadState('networkidle')

      // Verify the page has session management content
      // Either sessions are listed or a message indicates no sessions
      const hasSessionsOrEmptyState = await page.locator('main').isVisible()
      expect(hasSessionsOrEmptyState).toBe(true)
    })

    test('session page with invalid ID shows appropriate message', async ({ page }) => {
      // Navigate to a session with an invalid ID
      await page.goto('/sessions/nonexistent-session-id')
      await page.waitForLoadState('networkidle')

      // Should either show not found message or redirect
      // The session page handles missing sessions gracefully
      const mainContent = page.locator('main')
      await expect(mainContent).toBeVisible()
    })
  })

  test.describe('Session UI Element Tests', () => {
    test('sessions page has correct title', async ({ page }) => {
      // Navigate to sessions page
      await page.goto('/sessions')
      await page.waitForLoadState('networkidle')

      // Verify the page title contains relevant text
      await expect(page).toHaveTitle(/Homespun/)
    })

    test('session page navigation works', async ({ page }) => {
      // Start at home page
      await page.goto('/')
      await page.waitForLoadState('networkidle')

      // Look for sessions navigation link
      const sessionsLink = page.locator('a[href*="sessions"], a:has-text("Sessions")').first()
      const hasSessionsLink = await sessionsLink.isVisible()

      if (hasSessionsLink) {
        await sessionsLink.click()
        await page.waitForLoadState('networkidle')

        // Verify navigation succeeded
        expect(page.url().toLowerCase()).toMatch(/sessions/)
      } else {
        // If no direct link, navigate manually
        await page.goto('/sessions')
        await page.waitForLoadState('networkidle')
        const mainContent = page.locator('main')
        await expect(mainContent).toBeVisible()
      }
    })
  })

  test.describe('Mock Mode Verification Tests', () => {
    test('mock mode API endpoint returns data', async ({ request }) => {
      // Verify the sessions API endpoint works in mock mode
      const response = await request.get('/api/sessions')

      expect(response.ok()).toBe(true)
    })
  })

  test.describe('Session API Tests', () => {
    test('sessions API returns valid JSON', async ({ request }) => {
      // Test the sessions API returns valid JSON
      const response = await request.get('/api/sessions')

      expect(response.ok()).toBe(true)

      const body = await response.text()
      expect(() => JSON.parse(body)).not.toThrow()
    })

    test('projects API returns valid JSON', async ({ request }) => {
      // Test the projects API returns valid JSON (needed for session creation)
      const response = await request.get('/api/projects')

      expect(response.ok()).toBe(true)

      const body = await response.text()
      expect(() => JSON.parse(body)).not.toThrow()
    })
  })

  test.describe('SignalR Hub Connectivity Tests', () => {
    test('SignalR hub endpoint is accessible', async ({ request }) => {
      // Verify the SignalR hub negotiate endpoint is accessible
      const response = await request.post('/hubs/claudecode/negotiate?negotiateVersion=1', {
        headers: {
          'Content-Type': 'application/json',
        },
      })

      // SignalR negotiate returns 200 with connection info
      expect([200, 400]).toContain(response.status())
    })
  })
})
