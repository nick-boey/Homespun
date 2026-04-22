import { test, expect } from '@playwright/test'

/**
 * US1 — List and select projects.
 *
 * Mock mode seeds `demo-project` (see MockDataSeederService). The home page
 * lists all projects; clicking a card navigates into the project layout,
 * which redirects `/projects/{id}/` to `/projects/{id}/issues`.
 */
test.describe('Projects — list and select', () => {
  test('home page lists the seeded mock project', async ({ page }) => {
    await page.goto('/')
    await page.waitForLoadState('networkidle')

    const card = page.getByTestId('project-card-link').filter({ hasText: 'Demo Project' })
    await expect(card.first()).toBeVisible()
  })

  test('clicking a project card lands on /projects/{id}/issues', async ({ page }) => {
    await page.goto('/')
    await page.waitForLoadState('networkidle')

    await page.getByTestId('project-card-link').filter({ hasText: 'Demo Project' }).first().click()

    await page.waitForURL('**/projects/demo-project/issues')
    expect(page.url()).toContain('/projects/demo-project/issues')
  })
})
