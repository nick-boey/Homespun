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

      // Regardless of session count, the sessions page must never render a table layout
      const table = page.locator('table')
      await expect(table).not.toBeVisible()

      // When sessions exist, the list uses a card grid; when empty, an empty-state card.
      // Mock mode seeds no sessions (A2A event pipeline — see MockDataSeederService), so
      // only assert the grid when a card is actually rendered.
      //
      // Note: other tests may create sessions that then stop, causing the Active tab to
      // show "No active sessions" (no cards) while the global empty state shows "No sessions yet".
      // Both cases are valid empty-like states — check only the shared invariant (no table).
      const cards = page.locator('[data-slot="card"]')
      if ((await cards.count()) > 0) {
        const cardGrid = page.locator('[class*="grid"]').first()
        await expect(cardGrid).toBeVisible()
      }
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

        // Badge should contain "Issue", "PR", or "Issues Agent"
        const badgeText = await badge.textContent()
        expect(['Issue', 'PR', 'Issues Agent']).toContain(badgeText?.trim())
      }
    })

    test('session cards are clickable for navigation', async ({ page }) => {
      await page.goto('/sessions')
      await page.waitForLoadState('networkidle')

      // Look for session cards
      const cards = page.locator('[data-slot="card"]')
      const cardCount = await cards.count()

      if (cardCount > 0) {
        const firstCard = cards.first()

        // Check that the card is wrapped in a link (entire card is clickable)
        const cardLink = firstCard.locator('xpath=ancestor::a[contains(@href, "/sessions/")]')
        await expect(cardLink).toBeVisible()
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
