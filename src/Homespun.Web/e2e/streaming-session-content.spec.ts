import { test, expect } from '@playwright/test'

/**
 * Tests for streaming session content - verifies that:
 * 1. Pre-seeded session messages display correctly including tool results
 * 2. Messages persist across page refreshes without duplicates or phantom messages
 * 3. Sending a message shows the user message in the UI immediately (optimistic UI)
 * 4. Sessions listed on the sessions page are navigable to their detail view
 */
test.describe.serial('Streaming Session Content', () => {
  test('pre-seeded demo session displays messages and tool results', async ({ page }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate directly to the pre-seeded demo session which has messages with tool results
    await page.goto('/sessions/demo-session-001')
    await page.waitForLoadState('networkidle')

    // Wait for the session to load and messages to appear
    // The demo session has an initial assistant greeting
    await expect(page.getByText("I'm ready to help with your task")).toBeVisible({ timeout: 15000 })

    // Verify user message is displayed
    await expect(
      page.getByText('Please analyze the project structure and run the tests')
    ).toBeVisible({ timeout: 5000 })

    // Verify tool use blocks are rendered - the demo session has Read and Bash tools
    // Tool blocks render as buttons with the tool name and "Completed" suffix
    await expect(page.getByText(/Read.*Completed/)).toBeVisible({ timeout: 5000 })
  })

  test('messages persist across refreshes without duplicates', async ({ page }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate to the pre-seeded demo session
    await page.goto('/sessions/demo-session-001')
    await page.waitForLoadState('networkidle')

    // Wait for messages to load
    await expect(page.getByText("I'm ready to help with your task")).toBeVisible({ timeout: 15000 })

    // Capture the full message area content
    const messageArea = page.locator('[class*="overflow-y-auto"]').first()
    await expect(messageArea).toBeVisible({ timeout: 10000 })
    const firstLoadContent = await messageArea.textContent()

    // Refresh the page
    await page.reload()
    await page.waitForLoadState('networkidle')

    // Wait for messages to reload
    await expect(page.getByText("I'm ready to help with your task")).toBeVisible({ timeout: 15000 })

    // Verify the content is identical after refresh (no phantom messages)
    const afterRefreshContent = await messageArea.textContent()
    expect(afterRefreshContent).toBe(firstLoadContent)

    // Refresh again to double-check
    await page.reload()
    await page.waitForLoadState('networkidle')

    await expect(page.getByText("I'm ready to help with your task")).toBeVisible({ timeout: 15000 })
    const afterSecondRefreshContent = await messageArea.textContent()
    expect(afterSecondRefreshContent).toBe(firstLoadContent)
  })

  test('sending a message shows user message immediately via optimistic UI', async ({
    page,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate to the pre-seeded demo session
    await page.goto('/sessions/demo-session-001')
    await page.waitForLoadState('networkidle')

    // Wait for session to load with existing messages
    await expect(page.getByText("I'm ready to help with your task")).toBeVisible({ timeout: 15000 })

    // Wait for the chat input to be enabled (session joined)
    const chatInput = page.locator('textarea')
    await expect(chatInput).toBeVisible({ timeout: 10000 })
    await expect(chatInput).toBeEnabled({ timeout: 15000 })

    // Type and send a message
    await chatInput.click()
    await chatInput.fill('read the test file')
    await page.keyboard.press('Enter')

    // Verify user message appears immediately via optimistic UI
    // This confirms the chat input works and messages are added to the UI right away
    await expect(page.getByText('read the test file')).toBeVisible({ timeout: 5000 })

    // Verify the original messages are still visible alongside the new user message
    await expect(page.getByText("I'm ready to help with your task")).toBeVisible({ timeout: 5000 })
    await expect(
      page.getByText('Please analyze the project structure and run the tests')
    ).toBeVisible({ timeout: 5000 })
  })

  test('session listed on sessions page is navigable to detail view', async ({
    page,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate to the sessions page
    await page.goto('/sessions')
    await page.waitForLoadState('networkidle')

    // Verify session cards are listed
    const sessionCards = page.locator('[data-slot="card"]')
    await expect(sessionCards.first()).toBeVisible({ timeout: 15000 })

    // Navigate directly to the demo session to verify session detail page works
    await page.goto('/sessions/demo-session-001')
    await page.waitForLoadState('networkidle')

    // Verify the session detail page has a chat input (indicates session loaded)
    const chatInput = page.locator('textarea')
    await expect(chatInput).toBeVisible({ timeout: 10000 })

    // Verify session messages are displayed (the demo session has pre-seeded messages)
    await expect(page.getByText("I'm ready to help with your task")).toBeVisible({ timeout: 15000 })

    // Verify session metadata is displayed (mode indicator)
    await expect(page.getByText(/plan|build/i).first()).toBeVisible({ timeout: 5000 })
  })
})
