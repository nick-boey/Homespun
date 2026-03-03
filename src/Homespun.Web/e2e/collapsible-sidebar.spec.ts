import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for the inline issue detail expansion.
 * Verifies that issue rows expand only on double-click, not on keyboard navigation.
 *
 * Mirrors: tests/Homespun.E2E.Tests/CollapsibleSidebarTests.cs
 */
test.describe('Collapsible Sidebar', () => {
  test('inline detail is collapsed by default', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Verify no rows are expanded by default
    const expandedRow = page.locator('.task-graph-row-expanded')
    await expect(expandedRow).not.toBeVisible()

    // Verify no inline detail panel is visible
    const inlineDetail = page.locator('[data-testid="inline-issue-detail"]')
    await expect(inlineDetail).not.toBeVisible()
  })

  test('keyboard navigation does not expand row', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press j to select the first issue using keyboard
    await page.keyboard.press('j')

    // Verify a row is selected (keyboard navigation works)
    const selectedRow = page.locator('.task-graph-row-selected')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Verify no row is expanded
    const expandedRow = page.locator('.task-graph-row-expanded')
    await expect(expandedRow).not.toBeVisible()

    // Verify no inline detail panel is visible
    const inlineDetail = page.locator('[data-testid="inline-issue-detail"]')
    await expect(inlineDetail).not.toBeVisible()
  })

  test('double clicking issue expands row', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('[data-testid="task-graph-issue-row"]').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Double-click on an issue row
    await taskGraphRow.dblclick()

    // Verify the row is expanded
    const expandedRow = page.locator('.task-graph-row-expanded')
    await expect(expandedRow).toBeVisible({ timeout: 5000 })

    // Verify inline detail panel is shown
    const inlineDetail = page.locator('[data-testid="inline-issue-detail"]')
    await expect(inlineDetail).toBeVisible({ timeout: 5000 })
  })

  test('double clicking expanded issue collapses row', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render and double-click an issue to expand
    const taskGraphRow = page.locator('[data-testid="task-graph-issue-row"]').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })
    await taskGraphRow.dblclick()

    // Verify row is expanded
    const expandedRow = page.locator('.task-graph-row-expanded')
    await expect(expandedRow).toBeVisible({ timeout: 5000 })

    // Double-click again to collapse
    await taskGraphRow.dblclick()

    // Verify the row is collapsed
    await expect(expandedRow).not.toBeVisible({ timeout: 5000 })

    // Verify inline detail panel is hidden
    const inlineDetail = page.locator('[data-testid="inline-issue-detail"]')
    await expect(inlineDetail).not.toBeVisible({ timeout: 5000 })
  })

  test('escape key collapses expanded row', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render and double-click an issue to expand
    const taskGraphRow = page.locator('[data-testid="task-graph-issue-row"]').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })
    await taskGraphRow.dblclick()

    // Verify row is expanded
    const expandedRow = page.locator('.task-graph-row-expanded')
    await expect(expandedRow).toBeVisible({ timeout: 5000 })

    // Press Escape to collapse
    await page.keyboard.press('Escape')

    // Verify the row is collapsed
    await expect(expandedRow).not.toBeVisible({ timeout: 5000 })

    // Verify inline detail panel is hidden
    const inlineDetail = page.locator('[data-testid="inline-issue-detail"]')
    await expect(inlineDetail).not.toBeVisible({ timeout: 5000 })
  })

  test('keyboard navigation does not change expanded content when expanded', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const issueRows = page.locator('[data-testid="task-graph-issue-row"]')
    await expect(issueRows.first()).toBeVisible({ timeout: 10000 })

    // Double-click on the first issue to expand
    const firstRow = issueRows.first()
    await firstRow.dblclick()

    // Verify row is expanded and inline detail is shown
    const expandedRow = page.locator('.task-graph-row-expanded')
    await expect(expandedRow).toBeVisible({ timeout: 5000 })

    // Get the issue ID shown in the inline detail panel
    const inlineDetail = page.locator('[data-testid="inline-issue-detail"]')
    const expandedIssueId = await firstRow.getAttribute('data-issue-id')

    // Use keyboard to navigate to a different issue
    await page.keyboard.press('j')

    // Verify the original row is still expanded (expansion doesn't change on navigation)
    const stillExpandedRow = page.locator(
      `[data-issue-id="${expandedIssueId}"].task-graph-row-expanded`
    )
    await expect(stillExpandedRow).toBeVisible({ timeout: 5000 })

    // Verify inline detail panel is still visible
    await expect(inlineDetail).toBeVisible({ timeout: 5000 })
  })
})
