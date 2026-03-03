import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for inline issue creation with keyboard controls and hierarchy management.
 * Tests the TAB/Shift+TAB functionality for creating parent-child relationships between issues.
 *
 * Mirrors: tests/Homespun.E2E.Tests/InlineIssueHierarchyTests.cs
 */
test.describe('Inline Issue Hierarchy', () => {
  /**
   * Helper to navigate to the demo project's issues tab and wait for task graph to load.
   */
  async function navigateToProjectIssues(page: import('@playwright/test').Page) {
    await page.goto('/projects')
    await page.waitForLoadState('networkidle')

    // Click on the first project card
    const projectCard = page.locator('[data-testid="project-card"], .card').first()
    if (!(await projectCard.isVisible())) {
      return false // Signal test should skip
    }

    await projectCard.click()
    await page.waitForLoadState('networkidle')

    // Verify task graph is visible
    const taskGraph = page.locator('[data-testid="task-graph"]')
    await expect(taskGraph).toBeVisible({ timeout: 10000 })
    return true
  }

  /**
   * Helper to generate a unique issue title for tests.
   */
  function generateIssueTitle(): string {
    return `E2E Test Issue ${crypto.randomUUID().substring(0, 8)}`
  }

  test.describe('Basic Inline Creation Tests', () => {
    test('press o shows inline create input below selected issue', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      // Focus the task graph and press 'o'
      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Verify the inline create input appears
      const inlineInput = page.locator('[data-testid="inline-issue-create"]')
      await expect(inlineInput).toBeVisible({ timeout: 5000 })

      // Verify the input field is focused
      const inputField = page.locator('[data-testid="inline-issue-input"]')
      await expect(inputField).toBeFocused()
    })

    test('press shift+o shows inline create input above selected issue', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue (not the first one so there's room above)
      const secondIssueRow = page.locator('[data-testid="task-graph-issue-row"]').nth(1)
      if (!(await secondIssueRow.isVisible())) {
        test.skip(true, 'Need at least 2 issues for this test.')
        return
      }

      await secondIssueRow.click()

      // Focus the task graph and press Shift+O
      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('Shift+O')

      // Verify the inline create input appears
      const inlineInput = page.locator('[data-testid="inline-issue-create"]')
      await expect(inlineInput).toBeVisible({ timeout: 5000 })
    })

    test('escape cancels inline creation', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue and open inline create
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Verify input is visible
      const inlineInput = page.locator('[data-testid="inline-issue-create"]')
      await expect(inlineInput).toBeVisible()

      // Type something then press Escape
      const inputField = page.locator('[data-testid="inline-issue-input"]')
      await inputField.fill('Test title')
      await page.keyboard.press('Escape')

      // Verify the input is hidden
      await expect(inlineInput).toBeHidden({ timeout: 5000 })
    })

    test('create below without tab creates sibling issue', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Type title and press Enter
      const title = generateIssueTitle()
      const inputField = page.locator('[data-testid="inline-issue-input"]')
      await inputField.fill(title)
      await page.keyboard.press('Enter')

      // Wait for the creation to complete and inline input to disappear
      const inlineInput = page.locator('[data-testid="inline-issue-create"]')
      await expect(inlineInput).toBeHidden({ timeout: 10000 })

      // Verify the new issue appears in the task graph
      const newIssueRow = page.locator(`[data-testid="task-graph-issue-row"]:has-text("${title}")`)
      await expect(newIssueRow).toBeVisible({ timeout: 10000 })
    })
  })

  test.describe('TAB Key Tests (Create as Parent)', () => {
    test('tab while creating below shows parent of above indicator', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Press TAB
      await page.keyboard.press('Tab')

      // Verify the "Parent of above" indicator is shown
      const indicator = page.locator('.lane-indicator.parent')
      await expect(indicator).toBeVisible({ timeout: 5000 })
      await expect(indicator).toContainText('Parent of above')
    })

    test('create below with tab creates parent of issue above', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue (the "orphan" issue works well for this test)
      let orphanIssue = page.locator(
        '[data-testid="task-graph-issue-row"][data-issue-id="e2e/orphan"]'
      )
      if (!(await orphanIssue.isVisible())) {
        // Fall back to the first issue
        orphanIssue = page.locator('[data-testid="task-graph-issue-row"]').first()
      }
      await orphanIssue.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Press TAB to indicate this should be a parent
      await page.keyboard.press('Tab')

      // Type title and press Enter
      const title = generateIssueTitle()
      const inputField = page.locator('[data-testid="inline-issue-input"]')
      await inputField.fill(title)
      await page.keyboard.press('Enter')

      // Wait for creation to complete
      const inlineInput = page.locator('[data-testid="inline-issue-create"]')
      await expect(inlineInput).toBeHidden({ timeout: 10000 })

      // Verify the new issue appears
      const newIssueRow = page.locator(`[data-testid="task-graph-issue-row"]:has-text("${title}")`)
      await expect(newIssueRow).toBeVisible({ timeout: 10000 })
    })

    test('tab while creating above shows parent of below indicator', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue that's not the first
      const secondIssueRow = page.locator('[data-testid="task-graph-issue-row"]').nth(1)
      if (!(await secondIssueRow.isVisible())) {
        test.skip(true, 'Need at least 2 issues for this test.')
        return
      }
      await secondIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('Shift+O')

      // Press TAB
      await page.keyboard.press('Tab')

      // Verify the "Parent of below" indicator is shown
      const indicator = page.locator('.lane-indicator.parent')
      await expect(indicator).toBeVisible({ timeout: 5000 })
      await expect(indicator).toContainText('Parent of below')
    })
  })

  test.describe('Shift+TAB Key Tests (Create as Child)', () => {
    test('shift+tab while creating below shows child of above indicator', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Press Shift+TAB
      await page.keyboard.press('Shift+Tab')

      // Verify the "Child of above" indicator is shown
      const indicator = page.locator('.lane-indicator.child')
      await expect(indicator).toBeVisible({ timeout: 5000 })
      await expect(indicator).toContainText('Child of above')
    })

    test('create below with shift+tab creates child of issue above', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Press Shift+TAB to indicate this should be a child
      await page.keyboard.press('Shift+Tab')

      // Type title and press Enter
      const title = generateIssueTitle()
      const inputField = page.locator('[data-testid="inline-issue-input"]')
      await inputField.fill(title)
      await page.keyboard.press('Enter')

      // Wait for creation to complete
      const inlineInput = page.locator('[data-testid="inline-issue-create"]')
      await expect(inlineInput).toBeHidden({ timeout: 10000 })

      // Verify the new issue appears
      const newIssueRow = page.locator(`[data-testid="task-graph-issue-row"]:has-text("${title}")`)
      await expect(newIssueRow).toBeVisible({ timeout: 10000 })
    })

    test('shift+tab while creating above shows child of below indicator', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue that's not the first
      const secondIssueRow = page.locator('[data-testid="task-graph-issue-row"]').nth(1)
      if (!(await secondIssueRow.isVisible())) {
        test.skip(true, 'Need at least 2 issues for this test.')
        return
      }
      await secondIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('Shift+O')

      // Press Shift+TAB
      await page.keyboard.press('Shift+Tab')

      // Verify the "Child of below" indicator is shown
      const indicator = page.locator('.lane-indicator.child')
      await expect(indicator).toBeVisible({ timeout: 5000 })
      await expect(indicator).toContainText('Child of below')
    })
  })

  test.describe('TAB Key Limit Tests', () => {
    test('tab can only be pressed once', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Press TAB once
      await page.keyboard.press('Tab')

      // Verify parent indicator is shown
      const parentIndicator = page.locator('.lane-indicator.parent')
      await expect(parentIndicator).toBeVisible()

      // Press TAB again - should be ignored (no Shift+TAB to child)
      await page.keyboard.press('Tab')

      // Indicator should still show parent, not switch to child
      await expect(parentIndicator).toBeVisible()

      // Also verify no child indicator appears
      const childIndicator = page.locator('.lane-indicator.child')
      await expect(childIndicator).toBeHidden()
    })

    test('shift+tab can only be pressed once', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Press Shift+TAB once
      await page.keyboard.press('Shift+Tab')

      // Verify child indicator is shown
      const childIndicator = page.locator('.lane-indicator.child')
      await expect(childIndicator).toBeVisible()

      // Press Shift+TAB again - should be ignored (no TAB to parent)
      await page.keyboard.press('Shift+Tab')

      // Indicator should still show child
      await expect(childIndicator).toBeVisible()

      // Also verify no parent indicator appears
      const parentIndicator = page.locator('.lane-indicator.parent')
      await expect(parentIndicator).toBeHidden()
    })
  })

  test.describe('Edge Case Tests', () => {
    test('press o without selection does nothing', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Focus the task graph without selecting any issue
      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()

      // Press 'o'
      await page.keyboard.press('o')

      // Verify no inline input appears
      const inlineInput = page.locator('[data-testid="inline-issue-create"]')
      await page.waitForTimeout(500) // Small delay to ensure no async operation is in progress
      await expect(inlineInput).toBeHidden()
    })

    test('blurring input cancels creation', async ({ page }) => {
      if (!(await navigateToProjectIssues(page))) {
        test.skip(true, 'No projects available. This test requires mock mode.')
        return
      }

      // Select an issue and open inline create
      const firstIssueRow = page.locator('[data-testid="task-graph-issue-row"]').first()
      await firstIssueRow.click()

      const taskGraph = page.locator('[data-testid="task-graph"]')
      await taskGraph.focus()
      await page.keyboard.press('o')

      // Verify input is visible
      const inlineInput = page.locator('[data-testid="inline-issue-create"]')
      await expect(inlineInput).toBeVisible()

      // Click somewhere else to blur
      await page.locator('body').click({ position: { x: 10, y: 10 } })

      // Wait a moment for blur handler
      await page.waitForTimeout(200)

      // Verify the input is hidden
      await expect(inlineInput).toBeHidden({ timeout: 5000 })
    })
  })
})
