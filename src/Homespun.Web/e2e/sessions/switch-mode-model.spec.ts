import { test, expect } from '@playwright/test'
import { createMockSession } from '../utils/test-helpers'

/**
 * FI-1 / US5: switch mode and model — toggle the Plan/Build tabs and the model
 * selector on an active session and assert the UI reflects the new state.
 *
 * The mode and model controls live on the chat composer (`chat-input.tsx`) and
 * dispatch hub methods `setSessionMode` / `setSessionModel`. The mock backend
 * accepts both. We assert the visible state, not the wire side-effect, because
 * the wire path is already covered by `useChangeSessionSettings` unit tests.
 */
test.describe('US5 — switch mode and model', () => {
  let sessionId: string

  test.beforeAll(async ({ request }) => {
    sessionId = await createMockSession(request)
  })

  test('mode tabs toggle between plan and build', async ({ page }, testInfo) => {
    testInfo.setTimeout(60000)

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    await expect(page.getByPlaceholder('Type a message...')).toBeEnabled({ timeout: 15000 })

    const planTab = page.getByTestId('mode-tab-plan')
    const buildTab = page.getByTestId('mode-tab-build')

    await expect(buildTab).toBeVisible()
    await expect(planTab).toBeVisible()

    await planTab.click()
    await expect(planTab).toHaveAttribute('data-state', 'active', { timeout: 5000 })

    await buildTab.click()
    await expect(buildTab).toHaveAttribute('data-state', 'active', { timeout: 5000 })
  })

  test('model selector trigger is reachable and not disabled', async ({ page }, testInfo) => {
    testInfo.setTimeout(60000)

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    await expect(page.getByPlaceholder('Type a message...')).toBeEnabled({ timeout: 15000 })

    const trigger = page.getByTestId('model-selector-trigger')
    await expect(trigger).toBeVisible({ timeout: 10000 })
    await expect(trigger).toBeEnabled({ timeout: 10000 })
  })
})
