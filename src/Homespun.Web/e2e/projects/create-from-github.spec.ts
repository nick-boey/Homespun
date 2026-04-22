import { test, expect } from '@playwright/test'

/**
 * US2 — Create a project from a GitHub repository.
 *
 * The real `ProjectService.CreateAsync` hits GitHub + shells out to git clone,
 * so we stub POST /api/projects to mimic a successful create. The UI then
 * navigates to /projects/{id}.
 */
test.describe('Projects — create from GitHub', () => {
  test('submits owner/repo form and navigates to the new project', async ({ page }) => {
    await page.route('**/api/projects', async (route) => {
      if (route.request().method() !== 'POST') {
        await route.fallback()
        return
      }
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'new-gh-project',
          name: 'example-repo',
          localPath: '/tmp/homespun/example-repo/main',
          gitHubOwner: 'octocat',
          gitHubRepo: 'example-repo',
          defaultBranch: 'main',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        }),
      })
    })

    await page.goto('/projects/new')
    await page.waitForLoadState('networkidle')

    await page.getByLabel('Project Name').fill('example-repo')
    await page.getByLabel('Repository').fill('octocat/example-repo')

    await page.getByRole('button', { name: /create project/i }).click()

    await page.waitForURL('**/projects/new-gh-project**')
    expect(page.url()).toContain('/projects/new-gh-project')
  })

  test('surfaces an API error when create fails', async ({ page }) => {
    await page.route('**/api/projects', async (route) => {
      if (route.request().method() !== 'POST') {
        await route.fallback()
        return
      }
      await route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({
          detail: "Could not fetch repository 'octocat/missing' from GitHub.",
        }),
      })
    })

    await page.goto('/projects/new')
    await page.waitForLoadState('networkidle')

    await page.getByLabel('Project Name').fill('missing')
    await page.getByLabel('Repository').fill('octocat/missing')

    await page.getByRole('button', { name: /create project/i }).click()

    await expect(page.getByRole('alert')).toContainText(/Could not fetch repository/i)
    expect(page.url()).toContain('/projects/new')
  })
})
