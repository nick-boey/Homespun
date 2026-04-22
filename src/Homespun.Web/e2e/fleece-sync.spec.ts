import { test, expect, type Page } from '@playwright/test'

/**
 * E2E coverage for the Fleece sync flow rendered by `PullSyncButton` on the
 * project detail page.
 *
 * Mock mode's MockFleeceIssuesSyncService always returns a fixed happy-path
 * payload, so each scenario installs `page.route` interceptors to return the
 * specific FleecePullResult / FleeceIssueSyncResult shapes we want to exercise.
 */

const FLEECE_PULL_URL = /\/api\/fleece-sync\/[^/]+\/pull$/
const FLEECE_SYNC_URL = /\/api\/fleece-sync\/[^/]+\/sync$/
const FLEECE_DISCARD_AND_PULL_URL = /\/api\/fleece-sync\/[^/]+\/discard-non-fleece-and-pull$/
const PR_SYNC_URL = /\/api\/projects\/[^/]+\/sync$/

async function stubPrSync(page: Page) {
  await page.route(PR_SYNC_URL, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ imported: 0, updated: 0 }),
    })
  })
}

test.describe('Fleece sync', () => {
  test.beforeEach(async ({ page }) => {
    await stubPrSync(page)
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')
  })

  test('pull happy path surfaces an up-to-date toast', async ({ page }) => {
    await page.route(FLEECE_PULL_URL, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          wasBehindRemote: false,
          commitsPulled: 0,
          issuesMerged: 0,
          hasNonFleeceChanges: false,
          hasMergeConflict: false,
        }),
      })
    })

    await page.getByRole('button', { name: 'Pull' }).click()

    await expect(page.getByText('Pull complete')).toBeVisible()
    await expect(page.getByText('Already up to date')).toBeVisible()
  })

  test('pull while behind remote reports commits pulled and issues merged', async ({ page }) => {
    const pullRequests: string[] = []

    await page.route(FLEECE_PULL_URL, async (route) => {
      pullRequests.push(route.request().url())
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          wasBehindRemote: true,
          commitsPulled: 3,
          issuesMerged: 2,
          hasNonFleeceChanges: false,
          hasMergeConflict: false,
        }),
      })
    })

    await page.getByRole('button', { name: 'Pull' }).click()

    await expect(page.getByText('Pull complete')).toBeVisible()
    await expect(page.getByText(/Pulled 3 commit\(s\)/)).toBeVisible()
    await expect(page.getByText(/Merged 2 issue\(s\)/)).toBeVisible()
    expect(pullRequests.length).toBe(1)
  })

  test('discard and pull after non-fleece conflict hits the discard endpoint', async ({ page }) => {
    // First call: soft-fail with non-fleece changes, which opens the conflict dialog.
    await page.route(FLEECE_PULL_URL, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: false,
          errorMessage: 'uncommitted non-fleece changes',
          wasBehindRemote: false,
          commitsPulled: 0,
          issuesMerged: 0,
          hasNonFleeceChanges: true,
          nonFleeceChangedFiles: ['src/app.ts', 'README.md'],
          hasMergeConflict: false,
        }),
      })
    })

    // Second call (after discard): behind remote, pulls cleanly. The UI preserves
    // `.fleece/` state by routing this through the dedicated discard-and-pull
    // endpoint — never a plain reset.
    let discardCalled = false
    await page.route(FLEECE_DISCARD_AND_PULL_URL, async (route) => {
      discardCalled = true
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          wasBehindRemote: true,
          commitsPulled: 1,
          issuesMerged: 0,
          hasNonFleeceChanges: false,
          hasMergeConflict: false,
        }),
      })
    })

    await page.getByRole('button', { name: 'Pull' }).click()

    // Conflict dialog lists the non-fleece files.
    await expect(page.getByText('Uncommitted changes conflict')).toBeVisible()
    await expect(page.getByText('src/app.ts')).toBeVisible()
    await expect(page.getByText('README.md')).toBeVisible()

    await page.getByRole('button', { name: /Discard Changes & Retry/ }).click()

    await expect(page.getByText('Pull complete')).toBeVisible()
    await expect(page.getByText(/Pulled 1 commit\(s\)/)).toBeVisible()
    expect(discardCalled).toBe(true)
  })

  test('sync from dropdown pushes local commits', async ({ page }) => {
    await page.route(FLEECE_SYNC_URL, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          filesCommitted: 2,
          pushSucceeded: true,
        }),
      })
    })

    await page.getByRole('button', { name: 'More sync options' }).click()
    await page.getByRole('menuitem', { name: /Sync/ }).click()

    await expect(page.getByText('Sync complete')).toBeVisible()
    await expect(page.getByText(/Committed 2 file\(s\)/)).toBeVisible()
    await expect(page.getByText('Pushed to remote')).toBeVisible()
  })
})
