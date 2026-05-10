import { test, expect } from '@playwright/test'
import { createMockSession } from '../utils/test-helpers'

/**
 * FI-1 / US3: send a "question" message — the mock backend emits an
 * `input-required` status update with `inputType=question` plus an
 * `SdkQuestionPendingMessage`, which the server translates into an
 * `ask_user_question` AG-UI tool call and transitions the session into
 * `WaitingForQuestionAnswer`. Submitting an answer dispatches
 * `answerQuestion` over SignalR; the server then synthesises a
 * TOOL_CALL_RESULT envelope, which moves the renderer into receipt mode.
 */
test.describe('US3 — question / answer', () => {
  test('answering an AskUserQuestion transitions the renderer into receipt mode', async ({
    page,
    request,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    const sessionId = await createMockSession(request, { sendMessage: 'question' })

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    // The ask_user_question tool call is grouped inside a collapsible ToolGroup —
    // expand it before interacting with the question card.
    const toolGroupTrigger = page.locator('[data-slot="tool-group-trigger"]').first()
    await expect(toolGroupTrigger).toBeVisible({ timeout: 15000 })
    await toolGroupTrigger.click()

    const questionCard = page.getByTestId('ask-user-question-card')
    await expect(questionCard).toBeVisible({ timeout: 15000 })

    const submit = page.getByTestId('ask-user-question-submit')
    await expect(submit).toBeDisabled()

    const yesOption = page.getByTestId('ask-user-question-option-0-Yes')
    await expect(yesOption).toBeVisible()
    await yesOption.click()

    await expect(submit).toBeEnabled({ timeout: 10000 })
    await submit.click()

    await expect(page.getByText('Answered')).toBeVisible({ timeout: 15000 })
    await expect(questionCard).toHaveCount(0)
  })
})
