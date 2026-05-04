import { test, expect } from '@playwright/test'
import { createMockSession } from '../utils/test-helpers'

/**
 * FI-1 / US1: stream a message — create a session via UI/API, send a message, and
 * assert the streamed AG-UI envelope content surfaces in the message list.
 *
 * The AppHost mock backend canonically responds to `tool` with a Read tool-use
 * sequence; this spec asserts the user message echoes immediately (optimistic
 * UI) and that the assistant's tool-result content lands in the rendered
 * message stream.
 */
test.describe('US1 — stream a message', () => {
  let sessionId: string

  test.beforeAll(async ({ request }) => {
    sessionId = await createMockSession(request, { sendMessage: 'tool' })
  })

  test('user message + tool-call result stream into the message list', async ({
    page,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    await page.waitForSelector('[data-testid^="message-"]', { timeout: 15000 })

    const toolGroupTrigger = page.locator('[data-slot="tool-group-trigger"]').first()
    await expect(toolGroupTrigger).toBeVisible({ timeout: 5000 })
    await toolGroupTrigger.click()

    await expect(page.getByText(/mock\.txt/).first()).toBeVisible({ timeout: 5000 })
    await expect(page.getByText(/mock file contents/).first()).toBeVisible({ timeout: 5000 })

    const composer = page.getByPlaceholder('Type a message...')
    await expect(composer).toBeVisible({ timeout: 10000 })
    await expect(composer).toBeEnabled({ timeout: 15000 })
    await composer.click()
    await composer.fill('hello from US1')
    await page.keyboard.press('Enter')

    await expect(page.getByText('hello from US1')).toBeVisible({ timeout: 5000 })
  })
})
