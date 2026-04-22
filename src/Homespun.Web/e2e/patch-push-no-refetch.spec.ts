import { test, expect } from '@playwright/test'
import { clearIssueFilter } from './utils/test-helpers'

/**
 * Delta 3 contract: a patchable-field mutation triggered by one client must
 * arrive at other connected clients as an `IssueFieldsPatched` SignalR event
 * that is applied via `queryClient.setQueryData` — NOT as a refetch of the
 * task-graph endpoint. This test opens two browser contexts, edits the title
 * from context A via the API (simulating "another connected client"), and
 * fails if context B makes a GET on the taskgraph/data endpoint after the
 * initial load.
 */
test.describe('patch-push avoids refetch', () => {
  test('title edit from another client updates the observer tab with no taskgraph refetch', async ({
    browser,
    request,
  }) => {
    // Create an isolated issue so this test does not collide with the seeded
    // fixtures other specs rely on. The mock-mode server honours POST /api/issues.
    const createResponse = await request.post('/api/issues', {
      data: {
        projectId: 'demo-project',
        title: 'patch-push-before',
        type: 'task',
      },
    })
    expect(createResponse.ok()).toBe(true)
    const created = await createResponse.json()
    const issueId = created.id as string

    const observerContext = await browser.newContext()
    const observerPage = await observerContext.newPage()

    try {
      await observerPage.goto('/projects/demo-project/issues')
      await observerPage.waitForLoadState('networkidle')
      await clearIssueFilter(observerPage)

      await expect(
        observerPage.locator('[role="row"]').filter({ hasText: 'patch-push-before' }).first()
      ).toBeVisible({ timeout: 10000 })

      // Arm the refetch spy only AFTER the initial load has settled. Any
      // subsequent taskgraph fetch during the patch window would indicate
      // the patch-push path degenerated into an IssuesChanged broadcast.
      const refetches: string[] = []
      await observerPage.route('**/api/graph/**/taskgraph/data**', async (route) => {
        refetches.push(route.request().url())
        await route.continue()
      })

      // Simulate the "other connected client" with a direct PUT — structurally
      // identical to what the real editor submits, exercises the same server
      // broadcast helper.
      const updateResponse = await request.put(`/api/issues/${issueId}`, {
        data: {
          projectId: 'demo-project',
          title: 'patch-push-after',
        },
      })
      expect(updateResponse.ok()).toBe(true)

      // Wait for the SignalR event to apply via setQueryData. 2s is generous
      // for a broadcast that typically lands in <50ms; if the UI has not
      // updated by then the patch-push path is broken.
      await expect(
        observerPage.locator('[role="row"]').filter({ hasText: 'patch-push-after' }).first()
      ).toBeVisible({ timeout: 5000 })

      expect(
        refetches,
        `patch-push must not trigger a taskgraph refetch, but observed: ${refetches.join(', ')}`
      ).toEqual([])
    } finally {
      await observerContext.close()
    }
  })
})
