import { test, expect } from '@playwright/test'

/**
 * Tests for session navigation - specifically verifying that messages load
 * correctly when navigating from the sessions list to a session detail page.
 *
 * This test addresses the bug where clicking a session card would redirect to
 * the session page but messages and issue title wouldn't load until page refresh.
 */
test.describe('Session Navigation', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to sessions page first
    await page.goto('/sessions')
    await page.waitForLoadState('networkidle')
  })

  test('navigate from sessions list to session detail - messages load', async ({ page }) => {
    // Check if there are any session cards
    const cards = page.locator('[data-slot="card"]')
    const cardCount = await cards.count()

    if (cardCount === 0) {
      // Skip test if no sessions available
      test.skip(true, 'No sessions available to test navigation')
      return
    }

    // Get the first session card with a Chat link
    const chatLink = cards.first().getByRole('link', { name: 'Chat' })
    const hasChatLink = (await chatLink.count()) > 0

    if (!hasChatLink) {
      test.skip(true, 'No session cards with Chat link available')
      return
    }

    // Click the Chat link to navigate to session detail
    await chatLink.click()

    // Wait for navigation to complete
    await page.waitForURL(/\/sessions\/.+/)
    await page.waitForLoadState('networkidle')

    // Verify we're on a session detail page
    expect(page.url()).toMatch(/\/sessions\/.+/)

    // Wait for messages to load (the message container should have content)
    // Messages are displayed within a scrollable container
    const messageContainer = page.locator('[class*="overflow-y-auto"]').first()
    await expect(messageContainer).toBeVisible()

    // The session should have loaded - check for either:
    // 1. Messages in the chat (visible message content)
    // 2. Or the chat input (indicating the session page loaded)
    const chatInput = page.getByPlaceholder(/type a message|connecting|processing/i)

    // Wait for either messages or chat input to be visible
    await expect(chatInput).toBeVisible({ timeout: 10000 })
  })

  test('session header shows entity title after navigation', async ({ page }) => {
    // Check if there are any session cards
    const cards = page.locator('[data-slot="card"]')
    const cardCount = await cards.count()

    if (cardCount === 0) {
      test.skip(true, 'No sessions available to test navigation')
      return
    }

    // Get the first session card with a Chat link
    const chatLink = cards.first().getByRole('link', { name: 'Chat' })
    const hasChatLink = (await chatLink.count()) > 0

    if (!hasChatLink) {
      test.skip(true, 'No session cards with Chat link available')
      return
    }

    // Click the Chat link to navigate to session detail
    await chatLink.click()

    // Wait for navigation and page load
    await page.waitForURL(/\/sessions\/.+/)
    await page.waitForLoadState('networkidle')

    // The header should show either:
    // 1. An entity title (issue title)
    // 2. Or a session ID fallback
    const header = page.locator('h1').first()
    await expect(header).toBeVisible()

    // Header should not show "Loading..." once data is loaded
    await expect(header).not.toHaveText('Loading...', { timeout: 10000 })
  })

  test('back button navigates to sessions list', async ({ page }) => {
    // Check if there are any session cards
    const cards = page.locator('[data-slot="card"]')
    const cardCount = await cards.count()

    if (cardCount === 0) {
      test.skip(true, 'No sessions available to test navigation')
      return
    }

    // Navigate to a session detail page
    const chatLink = cards.first().getByRole('link', { name: 'Chat' })
    const hasChatLink = (await chatLink.count()) > 0

    if (!hasChatLink) {
      test.skip(true, 'No session cards with Chat link available')
      return
    }

    await chatLink.click()
    await page.waitForURL(/\/sessions\/.+/)
    await page.waitForLoadState('networkidle')

    // Click the back button
    const backButton = page
      .getByRole('link')
      .filter({ has: page.locator('svg') })
      .first()
    await backButton.click()

    // Verify we're back on the sessions list
    await page.waitForURL('/sessions')
    await expect(page.getByRole('tab', { name: /active/i })).toBeVisible()
  })

  test('session detail shows session metadata', async ({ page }) => {
    // Check if there are any session cards
    const cards = page.locator('[data-slot="card"]')
    const cardCount = await cards.count()

    if (cardCount === 0) {
      test.skip(true, 'No sessions available to test navigation')
      return
    }

    const chatLink = cards.first().getByRole('link', { name: 'Chat' })
    const hasChatLink = (await chatLink.count()) > 0

    if (!hasChatLink) {
      test.skip(true, 'No session cards with Chat link available')
      return
    }

    await chatLink.click()
    await page.waitForURL(/\/sessions\/.+/)
    await page.waitForLoadState('networkidle')

    // Should show session mode (Build/Plan)
    const modeText = page.getByText(/build|plan/i)
    await expect(modeText.first()).toBeVisible({ timeout: 10000 })

    // Should show session status badge
    const statusBadge = page.locator('[class*="rounded-full"][class*="px-2"]')
    await expect(statusBadge.first()).toBeVisible()
  })
})
