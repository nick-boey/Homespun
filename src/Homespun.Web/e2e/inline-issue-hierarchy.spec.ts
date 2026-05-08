import { test, expect } from '@playwright/test'
import { clearIssueFilter } from './utils/test-helpers'

/**
 * End-to-end tests for inline issue creation with keyboard controls and
 * hierarchy management (Tab / Shift+Tab to promote/demote).
 *
 * Mirrors: tests/Homespun.E2E.Tests/InlineIssueHierarchyTests.cs
 *
 * Navigation helpers:
 *   - `[data-testid="task-graph"]`             — the graph container (receives key events)
 *   - `[data-testid="task-graph-issue-row"]`   — individual issue rows
 *   - `[data-testid="task-graph-inline-create-row"]` — pending inline create row
 *   - `[data-testid="inline-issue-input"]`     — the text input inside the editor
 *   - `[data-testid="inline-issue-create"]`    — the editor wrapper
 *   - `.lane-indicator`                        — hierarchy indicator badge
 */

/** Navigate to the demo project issues page and wait for the graph. */
async function gotoIssues(
  page: Parameters<typeof test>[1] extends (args: infer A) => unknown
    ? A extends { page: infer P }
      ? P
      : never
    : never
) {
  await page.goto('/projects/demo-project/issues')
  await page.waitForLoadState('networkidle')
  await clearIssueFilter(page)
  // Wait for the task-graph to be present.
  await page.locator('[data-testid="task-graph"]').waitFor({ state: 'visible' })
}

/** Click the first visible issue row to select it. */
async function selectFirstIssue(
  page: Parameters<typeof test>[1] extends (args: infer A) => unknown
    ? A extends { page: infer P }
      ? P
      : never
    : never
) {
  const firstRow = page.locator('[data-testid="task-graph-issue-row"]').first()
  await firstRow.click()
  return firstRow
}

test.describe('Inline Issue Hierarchy', () => {
  test.describe('Basic Inline Creation Tests', () => {
    test('press o shows inline create input below selected issue', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      // Press 'o' to open inline create below the selected issue.
      await page.keyboard.press('o')

      // The pending row and its input should appear.
      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).toBeVisible()
      await expect(page.locator('[data-testid="inline-issue-input"]')).toBeVisible()
      await expect(page.locator('[data-testid="inline-issue-input"]')).toBeFocused()
    })

    test('press shift+o shows inline create input above selected issue', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      // Press 'O' (Shift+o) to open inline create above the selected issue.
      await page.keyboard.press('O')

      // The pending row and its input should appear.
      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).toBeVisible()
      await expect(page.locator('[data-testid="inline-issue-input"]')).toBeVisible()
      await expect(page.locator('[data-testid="inline-issue-input"]')).toBeFocused()
    })

    test('escape cancels inline creation', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      await page.keyboard.press('o')
      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).toBeVisible()

      // Press Escape to cancel.
      await page.keyboard.press('Escape')

      // The inline create row should be gone.
      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).not.toBeVisible()
    })

    test('create below without tab creates sibling issue', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      const uniqueTitle = `sibling-below-${Date.now()}`
      await page.keyboard.press('o')
      await page.locator('[data-testid="inline-issue-input"]').fill(uniqueTitle)
      await page.keyboard.press('Enter')

      // The inline create row should close.
      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).not.toBeVisible()

      // The newly created issue should appear somewhere in the graph.
      await expect(
        page
          .locator('[data-testid="task-graph-issue-row"]')
          .filter({ hasText: uniqueTitle })
          .first()
      ).toBeVisible({ timeout: 5000 })
    })
  })

  test.describe('TAB Key Tests (Create as Parent)', () => {
    test('tab while creating below shows parent-of indicator', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      await page.keyboard.press('o')
      await page.locator('[data-testid="inline-issue-input"]').fill('new parent')

      // Tab once → promotes to 'parent-of' mode.
      await page.keyboard.press('Tab')

      // The hierarchy indicator badge should show "Parent of".
      const indicator = page.locator('[data-testid="inline-issue-create"] .lane-indicator')
      await expect(indicator).toBeVisible()
      await expect(indicator).toHaveText('Parent of')

      // Cancel to clean up.
      await page.keyboard.press('Escape')
    })

    test('create below with tab creates parent-of issue', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      const uniqueTitle = `parent-of-${Date.now()}`
      await page.keyboard.press('o')
      await page.locator('[data-testid="inline-issue-input"]').fill(uniqueTitle)

      // Promote to parent-of mode.
      await page.keyboard.press('Tab')
      await page.keyboard.press('Enter')

      // The inline editor should close.
      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).not.toBeVisible()

      // The new issue should be visible in the graph.
      await expect(
        page
          .locator('[data-testid="task-graph-issue-row"]')
          .filter({ hasText: uniqueTitle })
          .first()
      ).toBeVisible({ timeout: 5000 })
    })

    test('tab while creating above shows parent-of indicator', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      await page.keyboard.press('O')
      await page.locator('[data-testid="inline-issue-input"]').fill('new parent above')

      // Tab once → promotes from sibling-above to parent-of.
      await page.keyboard.press('Tab')

      const indicator = page.locator('[data-testid="inline-issue-create"] .lane-indicator')
      await expect(indicator).toBeVisible()
      await expect(indicator).toHaveText('Parent of')

      await page.keyboard.press('Escape')
    })
  })

  test.describe('Shift+TAB Key Tests (Create as Child)', () => {
    test('shift+tab while creating below shows child-of indicator', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      await page.keyboard.press('o')
      await page.locator('[data-testid="inline-issue-input"]').fill('new child')

      // Shift+Tab → demotes to child-of mode.
      await page.keyboard.press('Shift+Tab')

      const indicator = page.locator('[data-testid="inline-issue-create"] .lane-indicator')
      await expect(indicator).toBeVisible()
      await expect(indicator).toHaveText('Child of')

      await page.keyboard.press('Escape')
    })

    test('create below with shift+tab creates child-of issue', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      const uniqueTitle = `child-of-${Date.now()}`
      await page.keyboard.press('o')
      await page.locator('[data-testid="inline-issue-input"]').fill(uniqueTitle)

      // Demote to child-of mode.
      await page.keyboard.press('Shift+Tab')
      await page.keyboard.press('Enter')

      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).not.toBeVisible()

      await expect(
        page
          .locator('[data-testid="task-graph-issue-row"]')
          .filter({ hasText: uniqueTitle })
          .first()
      ).toBeVisible({ timeout: 5000 })
    })

    test('shift+tab while creating above shows child-of indicator', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      await page.keyboard.press('O')
      await page.locator('[data-testid="inline-issue-input"]').fill('new child above')

      // Shift+Tab → promotes from sibling-above to child-of.
      await page.keyboard.press('Shift+Tab')

      const indicator = page.locator('[data-testid="inline-issue-create"] .lane-indicator')
      await expect(indicator).toBeVisible()
      await expect(indicator).toHaveText('Child of')

      await page.keyboard.press('Escape')
    })
  })

  test.describe('TAB Key Limit Tests', () => {
    test('tab can only be pressed once (second tab has no effect)', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      await page.keyboard.press('o')
      await page.locator('[data-testid="inline-issue-input"]').fill('title')

      // First Tab promotes to parent-of.
      await page.keyboard.press('Tab')
      const indicator = page.locator('[data-testid="inline-issue-create"] .lane-indicator')
      await expect(indicator).toHaveText('Parent of')

      // Second Tab should have no further effect (still parent-of).
      await page.keyboard.press('Tab')
      await expect(indicator).toHaveText('Parent of')

      await page.keyboard.press('Escape')
    })

    test('shift+tab can only be pressed once (second shift+tab has no effect)', async ({
      page,
    }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      await page.keyboard.press('o')
      await page.locator('[data-testid="inline-issue-input"]').fill('title')

      // First Shift+Tab demotes to child-of.
      await page.keyboard.press('Shift+Tab')
      const indicator = page.locator('[data-testid="inline-issue-create"] .lane-indicator')
      await expect(indicator).toHaveText('Child of')

      // Second Shift+Tab should have no further effect (still child-of).
      await page.keyboard.press('Shift+Tab')
      await expect(indicator).toHaveText('Child of')

      await page.keyboard.press('Escape')
    })
  })

  test.describe('Edge Case Tests', () => {
    test('press o without selection does nothing', async ({ page }) => {
      await gotoIssues(page)

      // Click somewhere neutral (the graph container but not a row).
      await page.locator('[data-testid="task-graph"]').click()

      // Deselect by navigating away briefly — press Escape to clear selection.
      await page.keyboard.press('Escape')

      // Press 'o' without any issue selected.
      await page.keyboard.press('o')

      // No inline create row should appear when there is no selection.
      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).not.toBeVisible()
    })

    test('clicking cancel button closes inline creation', async ({ page }) => {
      await gotoIssues(page)
      await selectFirstIssue(page)

      await page.keyboard.press('o')
      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).toBeVisible()

      // Click the cancel (X) button.
      await page.locator('[data-testid="inline-cancel-btn"]').click()

      await expect(page.locator('[data-testid="task-graph-inline-create-row"]')).not.toBeVisible()
    })
  })
})
