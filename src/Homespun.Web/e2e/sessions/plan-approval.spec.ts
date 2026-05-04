import { test, expect } from '@playwright/test'

/**
 * FI-1 / US2: trigger a plan via mock, assert PlanApprovalPanel mounts, approve,
 * assert plan executes.
 *
 * **Pending mock plumbing.** The current `MockAgentExecutionService` does not
 * emit an `ExitPlanMode` / `propose_plan` AG-UI event sequence on demand —
 * triggering one requires either:
 *
 *   1. extending `MockAgentExecutionService` to emit a plan event when the
 *      message contains a sentinel (e.g. `plan`), in the same way the existing
 *      `tool` sentinel triggers a Read tool-use; or
 *   2. driving the worker `/sessions/{id}/messages` POST with a canned A2A
 *      sequence via Playwright `page.route` interception of the worker URL.
 *
 * The data-testid wiring on the new `propose-plan.tsx` renderer
 * (`propose-plan-card`, `propose-plan-approve`, `propose-plan-reject`,
 * `propose-plan-feedback`) is already in place, so once the mock emits the
 * envelope sequence this spec only needs to flesh out the trigger.
 *
 * Tracked as a follow-up under FI-1 — see
 * `openspec/changes/close-out-claude-agent-sessions-migration-gaps/tasks.md`.
 */
test.describe('US2 — plan approval', () => {
  test.skip('approving a proposed plan dispatches approvePlan and executes', async () => {
    expect(true).toBe(true)
  })
})
