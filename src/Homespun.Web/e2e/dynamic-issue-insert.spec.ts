import { test, expect } from '@playwright/test'
import { clearIssueFilter } from './utils/test-helpers'

/**
 * After moving graph layout to the client, an issue created by *another*
 * connected client must arrive in the local view via the unified
 * `IssueChanged` SignalR event and apply through `applyIssueChanged` —
 * NOT via a refetch of `GET /api/projects/{projectId}/issues`. This test
 * opens an observer page, arms a refetch spy after the initial load, then
 * creates an issue via the API (simulating "another client") and asserts
 * that (a) the new node appears in the graph and (b) no `/issues` refetch
 * occurred during the dynamic-insert window.
 */
test.describe('dynamic issue insert', () => {
  test('issue created from another client appears via SignalR with no /issues refetch', async ({
    browser,
    request,
  }) => {
    const observerContext = await browser.newContext()
    const observerPage = await observerContext.newPage()

    try {
      await observerPage.goto('/projects/demo-project/issues')
      await observerPage.waitForLoadState('networkidle')
      await clearIssueFilter(observerPage)

      // Arm the refetch spy AFTER the initial load has settled. Any
      // subsequent `/issues` GET would indicate the dynamic-insert path
      // degenerated into a full visible-set refetch.
      const refetches: string[] = []
      await observerPage.route('**/api/projects/**/issues**', async (route) => {
        const req = route.request()
        if (req.method() === 'GET') {
          refetches.push(req.url())
        }
        await route.continue()
      })

      // Simulate the "other connected client" with a direct POST. This goes
      // through the same `BroadcastIssueChanged(Created, ...)` path the UI
      // uses, so the observer sees a SignalR event carrying the full issue
      // body.
      const uniqueTitle = `dynamic-insert-${Date.now()}`
      const createResponse = await request.post('/api/issues', {
        data: {
          projectId: 'demo-project',
          title: uniqueTitle,
          type: 'task',
        },
      })
      expect(createResponse.ok()).toBe(true)

      // The SignalR event carries the canonical issue and `applyIssueChanged`
      // merges it idempotently — no refetch needed. 5s is generous; live
      // round-trip typically lands in <100ms.
      await expect(
        observerPage.locator('[role="row"]').filter({ hasText: uniqueTitle }).first()
      ).toBeVisible({ timeout: 5000 })

      expect(
        refetches,
        `dynamic-insert must not trigger a /issues refetch, but observed: ${refetches.join(', ')}`
      ).toEqual([])
    } finally {
      await observerContext.close()
    }
  })
})
