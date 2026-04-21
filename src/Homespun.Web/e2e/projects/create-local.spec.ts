import { test, expect } from '@playwright/test'

/**
 * US3 — Create a local-only project.
 *
 * The real `ProjectService.CreateLocalAsync` runs git init/commit on disk;
 * in e2e we stub POST /api/projects so the flow stays hermetic.
 */
test.describe('Projects — create local', () => {
  test('submits the Local tab and navigates to the new project', async ({ page }) => {
    await page.route('**/api/projects', async (route) => {
      if (route.request().method() !== 'POST') {
        await route.fallback()
        return
      }
      const body = route.request().postDataJSON() as { name?: string; ownerRepo?: string }
      expect(body.name).toBe('my_local_project')
      expect(body.ownerRepo ?? null).toBeNull()

      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'new-local-project',
          name: 'my_local_project',
          localPath: '/tmp/homespun/my_local_project/main',
          defaultBranch: 'main',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        }),
      })
    })

    await page.goto('/projects/new')
    await page.waitForLoadState('networkidle')

    await page.getByRole('tab', { name: /local project/i }).click()
    await page.getByLabel('Project Name').fill('my_local_project')

    await page.getByRole('button', { name: /create project/i }).click()

    await page.waitForURL('**/projects/new-local-project**')
    expect(page.url()).toContain('/projects/new-local-project')
  })

  test('blocks submission when the project name has invalid characters', async ({ page }) => {
    await page.goto('/projects/new')
    await page.waitForLoadState('networkidle')

    await page.getByRole('tab', { name: /local project/i }).click()
    await page.getByLabel('Project Name').fill('bad name with spaces')

    await page.getByRole('button', { name: /create project/i }).click()

    await expect(page.getByRole('alert')).toContainText(
      /letters, numbers, hyphens, and underscores/i
    )
    expect(page.url()).toContain('/projects/new')
  })
})
