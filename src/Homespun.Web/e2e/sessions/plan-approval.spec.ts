import { test, expect } from '@playwright/test'
import { createMockSession } from '../utils/test-helpers'

/**
 * FI-1 / US2: send a "plan" message — the mock backend emits an `input-required`
 * status update with `inputType=plan-approval` plus an `SdkPlanPendingMessage`,
 * which the server translates into a `propose_plan` AG-UI tool call and
 * transitions the session into `WaitingForPlanExecution`. Approving from the
 * Tool UI renderer dispatches `approvePlan` over SignalR; the server then
 * synthesises a TOOL_CALL_RESULT envelope, which moves the renderer into
 * receipt mode.
 */
test.describe('US2 — plan approval', () => {
  test('approving a proposed plan transitions the renderer into receipt mode', async ({
    page,
    request,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    const sessionId = await createMockSession(request, { sendMessage: 'plan' })

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    // The propose_plan tool call is grouped inside a collapsible ToolGroup —
    // expand it before interacting with the plan-approval card.
    const toolGroupTrigger = page.locator('[data-slot="tool-group-trigger"]').first()
    await expect(toolGroupTrigger).toBeVisible({ timeout: 15000 })
    await toolGroupTrigger.click()

    const planCard = page.getByTestId('propose-plan-card')
    await expect(planCard).toBeVisible({ timeout: 15000 })

    const approve = page.getByTestId('propose-plan-approve')
    await expect(approve).toBeEnabled({ timeout: 10000 })
    await approve.click()

    await expect(page.getByText('Plan approved')).toBeVisible({ timeout: 15000 })
    await expect(planCard).toHaveCount(0)
  })

  test('rejecting a proposed plan with feedback transitions into receipt mode', async ({
    page,
    request,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    const sessionId = await createMockSession(request, { sendMessage: 'plan' })

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    const toolGroupTrigger = page.locator('[data-slot="tool-group-trigger"]').first()
    await expect(toolGroupTrigger).toBeVisible({ timeout: 15000 })
    await toolGroupTrigger.click()

    const planCard = page.getByTestId('propose-plan-card')
    await expect(planCard).toBeVisible({ timeout: 15000 })

    await page.getByTestId('propose-plan-feedback').fill('please revise step 2')

    const reject = page.getByTestId('propose-plan-reject')
    await expect(reject).toBeEnabled({ timeout: 10000 })
    await reject.click()

    await expect(page.getByText('Plan rejected')).toBeVisible({ timeout: 15000 })
    await expect(page.getByText('please revise step 2')).toBeVisible()
  })
})
