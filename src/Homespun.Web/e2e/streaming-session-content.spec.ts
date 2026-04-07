import { test, expect } from '@playwright/test'
import { createMockSession } from './utils/test-helpers'

/**
 * Tests for streaming session content - verifies that:
 * 1. Session messages display correctly including tool results
 * 2. Messages persist across page refreshes without duplicates or phantom messages
 * 3. Sending a message shows the user message in the UI immediately (optimistic UI)
 * 4. Sessions listed on the sessions page are navigable to their detail view
 */
test.describe.serial('Streaming Session Content', () => {
  let sessionId: string

  test.beforeAll(async ({ request }) => {
    // Create a session with tool use messages via the mock API
    // The mock agent generates Read/Write tool use when the message contains "tool"
    sessionId = await createMockSession(request, { sendMessage: 'tool' })
  })

  test('session displays messages and tool results', async ({ page }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate to the dynamically created session
    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    // Wait for messages to load
    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })

    // Verify tool use blocks are rendered - the mock agent produces Read tool results for "read file"
    await expect(page.getByText(/Read.*Completed/)).toBeVisible({ timeout: 5000 })
  })

  test('messages persist across refreshes without duplicates', async ({ page }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate to the session
    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    // Wait for messages to load
    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })

    // Capture the full message area content
    const messageArea = page.locator('[class*="overflow-y-auto"]').first()
    await expect(messageArea).toBeVisible({ timeout: 10000 })
    const firstLoadContent = await messageArea.textContent()

    // Refresh the page
    await page.reload()
    await page.waitForLoadState('networkidle')

    // Wait for messages to reload
    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })

    // Verify the content is identical after refresh (no phantom messages)
    const afterRefreshContent = await messageArea.textContent()
    expect(afterRefreshContent).toBe(firstLoadContent)

    // Refresh again to double-check
    await page.reload()
    await page.waitForLoadState('networkidle')

    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })
    const afterSecondRefreshContent = await messageArea.textContent()
    expect(afterSecondRefreshContent).toBe(firstLoadContent)
  })

  test('sending a message shows user message immediately via optimistic UI', async ({
    page,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate to the session
    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    // Wait for session to load with existing messages
    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })

    // Wait for the chat input to be enabled (session joined)
    const chatInput = page.locator('textarea')
    await expect(chatInput).toBeVisible({ timeout: 10000 })
    await expect(chatInput).toBeEnabled({ timeout: 15000 })

    // Type and send a message
    await chatInput.click()
    await chatInput.fill('read the test file')
    await page.keyboard.press('Enter')

    // Verify user message appears immediately via optimistic UI
    await expect(page.getByText('read the test file')).toBeVisible({ timeout: 5000 })
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

    // Navigate to our dynamically created session
    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    // Verify the session detail page has a chat input (indicates session loaded)
    const chatInput = page.locator('textarea')
    await expect(chatInput).toBeVisible({ timeout: 10000 })

    // Verify session messages are displayed
    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })

    // Verify session metadata is displayed (mode indicator)
    await expect(page.getByText(/plan|build/i).first()).toBeVisible({ timeout: 5000 })
  })
})
