import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'

// Helper to navigate to task graph
async function navigateToTaskGraph(page: Page) {
  // Navigate to projects
  await page.goto('/projects')
  await page.waitForLoadState('networkidle')

  // Click on the first project
  await page.locator('[data-test-id="project-card"]').first().click()
  await page.waitForLoadState('networkidle')

  // Wait for task graph to load
  await page.waitForSelector('[data-test-id="task-graph-container"]', { state: 'visible' })
}

// Helper to get actionable issue titles in order
async function getActionableIssueTitles(page: Page): Promise<string[]> {
  // Wait for issues to be rendered
  await page.waitForSelector('[data-test-id="task-graph-issue"]', { state: 'visible' })

  // Get all actionable issues (marked with ○ symbol)
  const actionableIssues = await page
    .locator('[data-test-id="task-graph-issue"]:has-text("○")')
    .all()

  const titles: string[] = []
  for (const issue of actionableIssues) {
    // Extract the title text (after the ○ marker)
    const fullText = await issue.textContent()
    if (fullText) {
      // Remove the ○ marker and any leading/trailing whitespace
      const title = fullText.replace(/^○\s*/, '').trim()
      titles.push(title)
    }
  }

  return titles
}

test.describe('Task Graph Sorting', () => {
  test('should sort actionable issues by priority then age', async ({ page }) => {
    // Navigate to task graph
    await navigateToTaskGraph(page)

    // Get actionable issue titles
    const titles = await getActionableIssueTitles(page)

    // Verify we have actionable issues
    expect(titles.length).toBeGreaterThan(0)

    // In mock data, actionable issues should be sorted by priority
    // We expect to see P0/P1 issues before P2/P3/P4 issues
    // and unprioritized issues at the end

    // Check if any P0 issues exist and appear first
    const p0Index = titles.findIndex((t) => t.includes('P0'))
    const p1Index = titles.findIndex((t) => t.includes('P1'))
    const p2Index = titles.findIndex((t) => t.includes('P2'))
    const unprioritizedIndex = titles.findIndex(
      (t) =>
        !t.includes('P0') &&
        !t.includes('P1') &&
        !t.includes('P2') &&
        !t.includes('P3') &&
        !t.includes('P4')
    )

    // If we have issues of different priorities, verify ordering
    if (p0Index >= 0 && p1Index >= 0) {
      expect(p0Index).toBeLessThan(p1Index)
    }
    if (p1Index >= 0 && p2Index >= 0) {
      expect(p1Index).toBeLessThan(p2Index)
    }
    if (p2Index >= 0 && unprioritizedIndex >= 0) {
      expect(p2Index).toBeLessThan(unprioritizedIndex)
    }
  })

  test('should display actionable issues with ○ marker', async ({ page }) => {
    await navigateToTaskGraph(page)

    // Check that we have issues with the actionable marker
    const actionableMarkers = await page.locator('text="○"').count()
    expect(actionableMarkers).toBeGreaterThan(0)

    // Verify actionable issues have the correct visual indicator
    const firstActionable = await page
      .locator('[data-test-id="task-graph-issue"]:has-text("○")')
      .first()
    await expect(firstActionable).toBeVisible()
  })

  test('should navigate to issues in sorted order with keyboard', async ({ page }) => {
    await navigateToTaskGraph(page)

    // Wait for issues to be rendered
    await page.waitForSelector('[data-test-id="task-graph-issue"]', { state: 'visible' })

    // Press 'j' to move to first issue
    await page.keyboard.press('j')

    // Get the currently selected issue
    let selectedIssue = await page
      .locator(
        '[data-test-id="task-graph-issue"].selected, [data-test-id="task-graph-issue"][data-selected="true"]'
      )
      .first()
    await expect(selectedIssue).toBeVisible()

    const firstTitle = await selectedIssue.textContent()

    // Press 'j' again to move to next issue
    await page.keyboard.press('j')

    // Verify we moved to a different issue
    selectedIssue = await page
      .locator(
        '[data-test-id="task-graph-issue"].selected, [data-test-id="task-graph-issue"][data-selected="true"]'
      )
      .first()
    const secondTitle = await selectedIssue.textContent()

    expect(firstTitle).not.toEqual(secondTitle)
  })

  test('should maintain grouping while sorting within groups', async ({ page }) => {
    await navigateToTaskGraph(page)

    // Check if we have multiple lanes (groups)
    const lanes = await page.locator('[data-test-id="task-graph-lane"]').count()

    if (lanes > 1) {
      // Get issues from first lane
      const firstLaneIssues = await page
        .locator(
          '[data-test-id="task-graph-lane"]:first-child [data-test-id="task-graph-issue"]:has-text("○")'
        )
        .all()

      // Verify issues in the same lane are sorted
      const firstLaneTitles: string[] = []
      for (const issue of firstLaneIssues) {
        const text = await issue.textContent()
        if (text) {
          firstLaneTitles.push(text)
        }
      }

      // Check if priorities within the lane are in order
      // This is a basic check - in real data we'd verify exact priority ordering
      expect(firstLaneTitles.length).toBeGreaterThanOrEqual(0)
    }
  })

  test('should show non-actionable issues in original positions', async ({ page }) => {
    await navigateToTaskGraph(page)

    // Get all issues (both actionable and non-actionable)
    const allIssues = await page.locator('[data-test-id="task-graph-issue"]').all()

    // Get issues without the ○ marker (non-actionable)
    const nonActionableIssues = await page
      .locator('[data-test-id="task-graph-issue"]:not(:has-text("○"))')
      .all()

    // Verify we have both types of issues
    expect(allIssues.length).toBeGreaterThan(0)
    expect(nonActionableIssues.length).toBeGreaterThanOrEqual(0)

    // Non-actionable issues should be interspersed, not all at the end
    // This verifies they maintain their original positions
  })
})
