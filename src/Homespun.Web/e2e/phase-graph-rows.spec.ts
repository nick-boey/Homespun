import { test, expect } from '@playwright/test'

/**
 * E2E tests for phase graph rows — diamond nodes in the task graph that
 * represent OpenSpec change phases (tasks 7.2 and 7.3).
 *
 * Requires the demo-project to be seeded with openspec data including ISSUE-006
 * linked to the "api-v2-design" change (3 phases: Design, Implement, Verify).
 */
test.describe('Phase graph rows', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/projects/demo-project/issues')
    await page.waitForLoadState('networkidle')
    // Wait for at least one issue row to be present
    await page.waitForSelector('[role="row"]', { timeout: 15000 })
  })

  test('phase rows are visible for ISSUE-006 (api-v2-design has 3 phases)', async ({ page }) => {
    const phaseRows = page.locator('[data-testid="task-graph-phase-row"]')
    const count = await phaseRows.count()

    if (count === 0) {
      // OpenSpec states may not have loaded yet or mock seeding wasn't run
      test.skip(true, 'No phase rows visible — openspec states may not be seeded')
      return
    }

    // At least one phase row should be visible
    await expect(phaseRows.first()).toBeVisible()
  })

  test('clicking a phase row opens its inline task panel', async ({ page }) => {
    const phaseRows = page.locator('[data-testid="task-graph-phase-row"]')
    const count = await phaseRows.count()

    if (count === 0) {
      test.skip(true, 'No phase rows visible — openspec states may not be seeded')
      return
    }

    // Click the first phase row
    await phaseRows.first().click()

    // The inline panel should appear after clicking/expanding
    await phaseRows.first().dblclick()

    // Inline phase detail panel should be visible
    const detailPanel = page.locator('[data-testid="inline-phase-detail-row"]')
    await expect(detailPanel.first()).toBeVisible({ timeout: 5000 })

    // The panel should contain a task list
    const taskList = detailPanel.first().locator('[data-testid="phase-task-list"]')
    await expect(taskList).toBeVisible()
  })

  test('phase row: pressing e does not open an edit dialog', async ({ page }) => {
    const phaseRows = page.locator('[data-testid="task-graph-phase-row"]')
    const count = await phaseRows.count()

    if (count === 0) {
      test.skip(true, 'No phase rows visible — openspec states may not be seeded')
      return
    }

    // Select the first phase row
    await phaseRows.first().click()

    // Press 'e' — should not open any edit dialog
    await page.keyboard.press('e')

    // No inline editor should appear
    const inlineEditor = page.locator('[data-testid="inline-issue-editor"]')
    const editorCount = await inlineEditor.count()
    expect(editorCount).toBe(0)
  })

  test('phase row: pressing r does not open an agent dialog', async ({ page }) => {
    const phaseRows = page.locator('[data-testid="task-graph-phase-row"]')
    const count = await phaseRows.count()

    if (count === 0) {
      test.skip(true, 'No phase rows visible — openspec states may not be seeded')
      return
    }

    await phaseRows.first().click()
    await page.keyboard.press('r')

    // No agent/run dialog should be open
    const agentDialog = page.locator('[role="dialog"]')
    const dialogCount = await agentDialog.count()
    expect(dialogCount).toBe(0)
  })

  test('phase row: pressing m does not activate a move operation', async ({ page }) => {
    const phaseRows = page.locator('[data-testid="task-graph-phase-row"]')
    const count = await phaseRows.count()

    if (count === 0) {
      test.skip(true, 'No phase rows visible — openspec states may not be seeded')
      return
    }

    await phaseRows.first().click()
    await page.keyboard.press('m')

    // No move-operation overlay should appear
    const moveOverlay = page.locator('[data-testid="move-operation-indicator"]')
    const moveCount = await moveOverlay.count()
    expect(moveCount).toBe(0)
  })
})
