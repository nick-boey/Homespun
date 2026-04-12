import { test, expect } from '@playwright/test'

test.describe('Clones Tab', () => {
  test('displays Clones tab in navigation', async ({ page }) => {
    await page.goto('/projects/demo-project/issues')
    const clonesTab = page.getByRole('link', { name: 'Clones' })
    await expect(clonesTab).toBeVisible()
  })

  test('navigates to Clones tab on click', async ({ page }) => {
    await page.goto('/projects/demo-project/issues')
    await page.click('a[href*="/clones"]')
    await expect(page).toHaveURL(/\/projects\/demo-project\/clones/)
  })

  test('displays empty state when no clones exist', async ({ page }) => {
    await page.route('**/api/projects/*/clones/enriched**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      })
    })

    await page.goto('/projects/demo-project/clones')
    await expect(page.getByText('No Clones Found')).toBeVisible()
  })

  test('displays Feature Clones section', async ({ page }) => {
    await page.route('**/api/projects/*/clones/enriched**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            clone: {
              path: '/path/to/clone1',
              branch: 'refs/heads/feature/test+ABC123',
              headCommit: 'abc1234567890',
              folderName: 'feature-test+ABC123',
            },
            linkedIssueId: 'ABC123',
            linkedIssue: {
              id: 'ABC123',
              title: 'Test Issue',
              status: 'open',
            },
            isDeletable: false,
            isIssuesAgentClone: false,
          },
        ]),
      })
    })

    await page.goto('/projects/demo-project/clones')
    await expect(page.getByText('Feature Clones')).toBeVisible()
    await expect(page.getByText('Test Issue')).toBeVisible()
  })

  test('displays Issues Agent Clones section separately', async ({ page }) => {
    await page.route('**/api/projects/*/clones/enriched**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            clone: {
              path: '/path/to/issues-agent',
              branch: 'refs/heads/issues-agent-20260322-120000',
              headCommit: 'def1234567890',
              folderName: 'issues-agent-20260322-120000',
            },
            isDeletable: true,
            deletionReason: 'Issues Agent session - can be cleaned up',
            isIssuesAgentClone: true,
          },
        ]),
      })
    })

    await page.goto('/projects/demo-project/clones')
    await expect(page.getByText('Issues Agent Clones')).toBeVisible()
  })

  test('shows delete button on deletable clones', async ({ page }) => {
    await page.route('**/api/projects/*/clones/enriched**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            clone: {
              path: '/path/to/deletable',
              branch: 'refs/heads/feature/merged+XYZ789',
              headCommit: 'ghi1234567890',
              folderName: 'feature-merged+XYZ789',
            },
            linkedPr: {
              number: 123,
              title: 'Merged PR',
              status: 'merged',
            },
            isDeletable: true,
            deletionReason: 'PR merged',
            isIssuesAgentClone: false,
          },
        ]),
      })
    })

    await page.goto('/projects/demo-project/clones')
    // The delete button on a card has a Trash2 icon inside a button
    const deleteButton = page.locator('button:has(svg.lucide-trash-2)').first()
    await expect(deleteButton).toBeVisible()
  })

  test('shows Delete All Stale button with count', async ({ page }) => {
    await page.route('**/api/projects/*/clones/enriched**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            clone: { path: '/path/1', branch: 'refs/heads/b1', headCommit: 'a', folderName: 'b1' },
            isDeletable: true,
            isIssuesAgentClone: false,
          },
          {
            clone: { path: '/path/2', branch: 'refs/heads/b2', headCommit: 'b', folderName: 'b2' },
            isDeletable: true,
            isIssuesAgentClone: false,
          },
          {
            clone: { path: '/path/3', branch: 'refs/heads/b3', headCommit: 'c', folderName: 'b3' },
            isDeletable: false,
            isIssuesAgentClone: false,
          },
        ]),
      })
    })

    await page.goto('/projects/demo-project/clones')
    await expect(page.getByRole('button', { name: /Delete All Stale \(2\)/i })).toBeVisible()
  })

  test('opens confirmation dialog on Delete All click', async ({ page }) => {
    await page.route('**/api/projects/*/clones/enriched**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            clone: { path: '/path/1', branch: 'refs/heads/b1', headCommit: 'a', folderName: 'b1' },
            isDeletable: true,
            isIssuesAgentClone: false,
          },
        ]),
      })
    })

    await page.goto('/projects/demo-project/clones')
    await page.getByRole('button', { name: /Delete All Stale/i }).click()
    await expect(page.getByText('Delete All Stale Clones')).toBeVisible()
  })

  test('deletes clones on confirmation', async ({ page }) => {
    let deleteApiCalled = false

    await page.route('**/api/projects/*/clones/enriched**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            clone: { path: '/path/1', branch: 'refs/heads/b1', headCommit: 'a', folderName: 'b1' },
            isDeletable: true,
            isIssuesAgentClone: false,
          },
        ]),
      })
    })

    await page.route('**/api/projects/*/clones/bulk**', async (route) => {
      deleteApiCalled = true
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ results: [{ clonePath: '/path/1', success: true }] }),
      })
    })

    await page.goto('/projects/demo-project/clones')
    await page.getByRole('button', { name: /Delete All Stale/i }).click()
    await page.getByRole('button', { name: 'Delete All' }).click()

    expect(deleteApiCalled).toBe(true)
  })

  test('displays linked PR with status badge', async ({ page }) => {
    await page.route('**/api/projects/*/clones/enriched**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            clone: {
              path: '/path/to/clone',
              branch: 'refs/heads/feature/test',
              headCommit: 'abc1234567890',
              folderName: 'feature-test',
            },
            linkedPr: {
              number: 456,
              title: 'Add new feature',
              status: 'readyForReview',
              htmlUrl: 'https://github.com/test/repo/pull/456',
            },
            isDeletable: false,
            isIssuesAgentClone: false,
          },
        ]),
      })
    })

    await page.goto('/projects/demo-project/clones')
    await expect(page.getByText('#456: Add new feature')).toBeVisible()
    await expect(page.getByText('readyForReview')).toBeVisible()
  })
})
