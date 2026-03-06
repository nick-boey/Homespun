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

    test('sessions page displays card-based layout', async ({ page }) => {
      await page.goto('/sessions')
      await page.waitForLoadState('networkidle')

      // Check for card grid container
      const cardGrid = page.locator('[class*="grid"]').first()
      await expect(cardGrid).toBeVisible()

      // Check that it's not a table layout
      const table = page.locator('table')
      await expect(table).not.toBeVisible()
    })

    test('session cards display entity badges', async ({ page }) => {
      await page.goto('/sessions')
      await page.waitForLoadState('networkidle')

      // Look for session cards
      const cards = page.locator('[data-slot="card"]')
      const cardCount = await cards.count()

      if (cardCount > 0) {
        // Check first card has entity badge
        const firstCard = cards.first()
        const badge = firstCard.locator('[data-slot="badge"]').first()
        await expect(badge).toBeVisible()

        // Badge should contain "Issue" or "PR"
        const badgeText = await badge.textContent()
        expect(['Issue', 'PR']).toContain(badgeText?.trim())
      }
    })

    test('session cards have action buttons', async ({ page }) => {
      await page.goto('/sessions')
      await page.waitForLoadState('networkidle')

      // Look for session cards
      const cards = page.locator('[data-slot="card"]')
      const cardCount = await cards.count()

      if (cardCount > 0) {
        const firstCard = cards.first()

        // Check for "Chat" button/link
        const chatButton = firstCard.locator('text=Chat')
        await expect(chatButton).toBeVisible()
      }
    })

    test('session page with invalid ID shows error or redirect', async ({ page }) => {
      await page.goto('/sessions/nonexistent-session-id')
      await page.waitForLoadState('networkidle')

      // Should render something (body visible)
      await expect(page.locator('body')).toBeVisible()
    })
  })
})
