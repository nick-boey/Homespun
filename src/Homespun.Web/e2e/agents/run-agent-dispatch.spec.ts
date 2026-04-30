import { test, expect } from '@playwright/test'

/**
 * FI-1 — Playwright coverage for the Run Agent dispatch flow over the live
 * Vite + .NET stack.
 *
 * The single-issue happy path through the Run Agent dialog is already exercised
 * by `agent-and-issue-agent-launching.spec.ts`. These specs add the contract
 * checks that were missing:
 *
 * - 202 envelope on `POST /api/issues/{id}/run` (mirrors the API integration
 *   test in tests/Homespun.Api.Tests/Features/AgentOrchestration/RunAgentApiTests.cs;
 *   re-asserted here through the browser-side request context so the Vite
 *   proxy and auth surface are also covered).
 * - 409 guard against an immediate double dispatch.
 * - 404 when the project does not exist.
 *
 * Extending the UI flow itself with rapid-double-click and blocked-base-branch
 * scenarios would require the mock service to emit deterministic failures via
 * SignalR, which it does not today. The route-agnostic
 * `AgentStartFailed` toast (FI-2) is verified at the unit level instead — see
 * `use-notification-events.test.tsx` "surfaces AgentStartFailed via a sonner
 * toast".
 */

test.describe('Run Agent dispatch — API surface', () => {
  test('returns 202 with the expected envelope on a single-issue dispatch', async ({ request }) => {
    const response = await request.post('/api/issues/ISSUE-013/run', {
      data: {
        projectId: 'demo-project',
        mode: 'plan',
      },
    })

    expect(response.status()).toBe(202)
    const body = await response.json()
    expect(body).toMatchObject({
      issueId: 'ISSUE-013',
      branchName: expect.any(String),
      message: expect.any(String),
    })
    expect(body.branchName.length).toBeGreaterThan(0)
  })

  test('rejects an immediate double-dispatch with 409', async ({ request }) => {
    const first = await request.post('/api/issues/ISSUE-011/run', {
      data: { projectId: 'demo-project', mode: 'plan' },
    })
    expect(first.status()).toBe(202)

    const second = await request.post('/api/issues/ISSUE-011/run', {
      data: { projectId: 'demo-project', mode: 'plan' },
    })

    expect(second.status()).toBe(409)
    const body = await second.json()
    expect(body).toMatchObject({ message: expect.any(String) })
  })

  test('returns 404 when the project does not exist', async ({ request }) => {
    const response = await request.post('/api/issues/ISSUE-013/run', {
      data: { projectId: 'no-such-project', mode: 'plan' },
    })
    expect(response.status()).toBe(404)
  })
})
