import { test, expect } from '@playwright/test'

test.describe('My Tasks Filter Toggle', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/projects')
    await page.locator('[data-testid="project-card-link"]').first().click()
    await page.waitForLoadState('networkidle')
    // Wait for toolbar and task graph to be fully rendered
    await page.locator('[data-testid="toolbar-my-tasks-button"]').waitFor({ state: 'visible' })
    await page.locator('[data-testid="task-graph-issue-row"]').first().waitFor({ state: 'visible' })
  })

  test('no filter is active on page load', async ({ page }) => {
    const myTasksButton = page.locator('[data-testid="toolbar-my-tasks-button"]')
    await expect(myTasksButton).toHaveAttribute('aria-pressed', 'false')

    // Filter input should not be visible
    await expect(page.locator('[data-testid="filter-input"]')).not.toBeVisible()
  })

  test('clicking My Tasks applies assigned:me filter and clicking again clears it', async ({
    page,
  }) => {
    const myTasksButton = page.locator('[data-testid="toolbar-my-tasks-button"]')

    // Click My Tasks to apply filter
    await myTasksButton.click()

    // Filter input should appear and show assigned:me
    const filterInput = page.locator('[data-testid="filter-input"]')
    await expect(filterInput).toBeVisible()
    await expect(filterInput).toHaveValue('assigned:me')

    // Button should show active state
    await expect(myTasksButton).toHaveAttribute('aria-pressed', 'true')

    // Click again to clear filter
    await myTasksButton.click()
    await expect(myTasksButton).toHaveAttribute('aria-pressed', 'false')

    // Filter input should no longer be visible
    await expect(filterInput).not.toBeVisible()
  })
})
