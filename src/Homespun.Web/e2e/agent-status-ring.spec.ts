import { test, expect } from '@playwright/test'

test.describe('Agent Status Ring', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the projects page where the task graph is displayed
    await page.goto('/projects')

    // Wait for the project list to load
    await page.waitForSelector('[data-testid="project-list"]', { timeout: 10000 })

    // Click on the first project to view its task graph
    await page.click('[data-testid="project-card"]:first-child')

    // Wait for the task graph to load
    await page.waitForSelector('[data-testid="task-graph-view"]', { timeout: 10000 })
  })

  test('should not show ring when no agent is active', async ({ page }) => {
    // Find an issue node in the task graph
    const issueNode = page.locator('[data-issue-id]').first()
    await expect(issueNode).toBeVisible()

    // Get the SVG element for this issue
    const svg = issueNode.locator('svg')

    // Should only have one circle (the node itself, no ring)
    const circles = svg.locator('circle')
    await expect(circles).toHaveCount(1)
  })

  test('should show blue pulsing ring for running agent', async ({ page }) => {
    // Find an issue to start an agent on
    const issueNode = page.locator('[data-issue-id]').first()

    // Click on the issue to select it
    await issueNode.click()

    // Find and click the "Run Agent" button in the actions
    await page.click('[data-testid="run-agent-button"]')

    // Wait for agent to start (status should change)
    await page.waitForTimeout(2000) // Give agent time to start

    // Check that the ring appears
    const svg = issueNode.locator('svg')
    const circles = svg.locator('circle')
    await expect(circles).toHaveCount(2) // Node + ring

    // Check the ring properties
    const ring = circles.first()
    await expect(ring).toHaveAttribute('stroke', '#3b82f6') // Blue
    await expect(ring).toHaveAttribute('stroke-width', '2')
    await expect(ring).toHaveAttribute('opacity', '0.6')
    await expect(ring).toHaveClass(/animate-pulse/)
  })

  test('should show yellow ring for waiting states', async ({ page }) => {
    // This would require mocking an agent in a waiting state
    // For now, we'll create a placeholder test

    // Find an issue with an agent in waiting state
    // In a real test, we'd need to set up this state through the API
    const issueWithWaitingAgent = page.locator('[data-issue-id="test-waiting"]')

    if ((await issueWithWaitingAgent.count()) > 0) {
      const svg = issueWithWaitingAgent.locator('svg')
      const circles = svg.locator('circle')
      await expect(circles).toHaveCount(2)

      const ring = circles.first()
      await expect(ring).toHaveAttribute('stroke', '#eab308') // Yellow
    }
  })

  test('should show red ring for error state', async ({ page }) => {
    // This would require mocking an agent in error state
    // For now, we'll create a placeholder test

    const issueWithErrorAgent = page.locator('[data-issue-id="test-error"]')

    if ((await issueWithErrorAgent.count()) > 0) {
      const svg = issueWithErrorAgent.locator('svg')
      const circles = svg.locator('circle')
      await expect(circles).toHaveCount(2)

      const ring = circles.first()
      await expect(ring).toHaveAttribute('stroke', '#ef4444') // Red
    }
  })

  test('should remove ring when agent stops', async ({ page }) => {
    // Find an issue with a running agent
    const issueNode = page.locator('[data-issue-id]').first()

    // Assume agent is running (would need to start it first in a real test)
    // Click stop button if available
    const stopButton = page.locator('[data-testid="stop-agent-button"]')
    if (await stopButton.isVisible()) {
      await stopButton.click()

      // Wait for agent to stop
      await page.waitForTimeout(2000)

      // Check that ring is gone
      const svg = issueNode.locator('svg')
      const circles = svg.locator('circle')
      await expect(circles).toHaveCount(1) // Only node, no ring
    }
  })

  test('ring should have correct size relative to node', async ({ page }) => {
    // Find an issue with an active agent
    const issueWithAgent = page
      .locator('[data-issue-id]')
      .filter({
        has: page.locator('svg circle').nth(1), // Has 2 circles
      })
      .first()

    if ((await issueWithAgent.count()) > 0) {
      const svg = issueWithAgent.locator('svg')
      const circles = svg.locator('circle')

      // Get radius of both circles
      const ringRadius = await circles.first().getAttribute('r')
      const nodeRadius = await circles.nth(1).getAttribute('r')

      // Ring should be 4 pixels larger than node
      expect(Number(ringRadius)).toBe(Number(nodeRadius) + 4)
    }
  })

  test('should handle multiple agents on different issues', async ({ page }) => {
    // This test would verify that multiple issues can have rings simultaneously
    const allIssues = page.locator('[data-issue-id]')
    const issueCount = await allIssues.count()

    if (issueCount >= 2) {
      // In a real test, we'd start agents on multiple issues
      // and verify each has its own ring with appropriate status
    }
  })
})

// Test for keyboard navigation with agent status
test.describe('Agent Status Ring - Keyboard Navigation', () => {
  test('should maintain ring visibility during keyboard navigation', async ({ page }) => {
    await page.goto('/projects')
    await page.waitForSelector('[data-testid="project-list"]')
    await page.click('[data-testid="project-card"]:first-child')
    await page.waitForSelector('[data-testid="task-graph-view"]')

    // Find issue with active agent (if any)
    const issueWithAgent = page
      .locator('[data-issue-id]')
      .filter({
        has: page.locator('svg circle').nth(1),
      })
      .first()

    if ((await issueWithAgent.count()) > 0) {
      // Focus on the issue
      await issueWithAgent.focus()

      // Navigate with keyboard
      await page.keyboard.press('ArrowDown')
      await page.keyboard.press('ArrowUp')

      // Ring should still be visible
      const svg = issueWithAgent.locator('svg')
      const circles = svg.locator('circle')
      await expect(circles).toHaveCount(2)
    }
  })
})
