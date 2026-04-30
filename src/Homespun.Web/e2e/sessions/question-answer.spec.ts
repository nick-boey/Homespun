import { test, expect } from '@playwright/test'

/**
 * FI-1 / US3: trigger an `AskUserQuestion` mock event, assert the panel renders,
 * answer, assert the answer is broadcast.
 *
 * **Pending mock plumbing.** Same situation as the plan-approval spec — the
 * `MockAgentExecutionService` does not yet emit an `ask_user_question` tool
 * call on demand. The data-testid wiring on `ask-user-question.tsx`
 * (`ask-user-question-card`, `ask-user-question-${idx}`,
 * `ask-user-question-option-${idx}-${label}`, `ask-user-question-submit`) is
 * already in place; the spec only needs the mock event trigger.
 *
 * Tracked as a follow-up under FI-1.
 */
test.describe('US3 — question / answer', () => {
  test.skip('answering an AskUserQuestion broadcasts the answer to the hub', async () => {
    expect(true).toBe(true)
  })
})
