import { test, expect } from '@playwright/test'
import { createMockSession } from '../utils/test-helpers'

/**
 * FI-1 / US4: list resumable sessions, navigate to a previously-stopped session,
 * and assert the replayed events render. The mock backend persists session
 * messages through its A2A event store, so navigating back into a session
 * after sending a message returns the same events via the replay endpoint.
 */
test.describe('US4 — resume a session', () => {
  test('replayed messages render on a fresh session-detail mount', async ({
    page,
    request,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    const sessionId = await createMockSession(request, { sendMessage: 'tool' })

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')
    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })

    const initialMessageArea = page.locator('[class*="overflow-y-auto"]').first()
    const initialContent = await initialMessageArea.textContent()
    expect(initialContent).toBeTruthy()

    await page.goto('/sessions')
    await page.waitForLoadState('networkidle')

    const sessionCards = page.locator('[data-slot="card"]')
    await expect(sessionCards.first()).toBeVisible({ timeout: 15000 })

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')
    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })

    const replayedMessageArea = page.locator('[class*="overflow-y-auto"]').first()
    const replayedContent = await replayedMessageArea.textContent()

    expect(replayedContent).toBe(initialContent)
  })
})
