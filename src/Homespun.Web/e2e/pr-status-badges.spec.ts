import { test, expect } from '@playwright/test'

test.describe('PR Status Badges', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the projects page
    await page.goto('/projects')
    await page.waitForLoadState('networkidle')

    // Check if there's a project, if not create one
    const projectCards = page.locator('[data-testid="project-card"]')
    const projectCount = await projectCards.count()

    if (projectCount === 0) {
      // Create a new project
      await page.getByRole('button', { name: 'New Project' }).click()
      await page.getByPlaceholder('Project name').fill('Test Project')
      await page.getByRole('button', { name: 'Create' }).click()
      await page.waitForURL('**/task-graph')
    } else {
      // Click on the first project
      await projectCards.first().click()
      await page.waitForURL('**/task-graph')
    }
  })

  test('shows PR status badges in task graph', async ({ page }) => {
    // Wait for task graph to load
    await page.waitForSelector('[role="row"]')

    // Look for any issue rows with PR links
    const rowsWithPR = await page.locator('[role="row"]').filter({
      has: page.locator('a[href*="/pull/"]'),
    })

    const rowCount = await rowsWithPR.count()
    if (rowCount === 0) {
      // No rows with PRs, skip test
      test.skip(true, 'No issues with linked PRs found')
      return
    }

    // Check the first row with a PR
    const firstRowWithPR = rowsWithPR.first()
    const prLink = firstRowWithPR.locator('a[href*="/pull/"]')

    // Verify PR link is visible
    await expect(prLink).toBeVisible()

    // Look for PR status indicators near the PR link
    const prStatusContainer = prLink.locator('..').first()

    // Check for merge conflict indicator (GitBranch icon)
    const mergeIndicator = prStatusContainer.locator('svg').filter({
      has: page.locator('[aria-label*="merge conflicts"]'),
    })

    // Check for CI status indicator (Check, X, or Loader)
    const ciIndicator = prStatusContainer.locator('svg').filter({
      has: page.locator('[aria-label*="Tests"]'),
    })

    // At least one of the indicators should be present
    const mergeIndicatorCount = await mergeIndicator.count()
    const ciIndicatorCount = await ciIndicator.count()

    expect(mergeIndicatorCount + ciIndicatorCount).toBeGreaterThan(0)
  })

  test('PR status badges have correct colors', async ({ page }) => {
    // Wait for task graph to load
    await page.waitForSelector('[role="row"]')

    // Look for merge conflict indicators
    const noConflictIndicators = page.locator('[aria-label="No merge conflicts"]')
    const hasConflictIndicators = page.locator('[aria-label="Has merge conflicts"]')

    // Check colors if indicators exist
    const noConflictCount = await noConflictIndicators.count()
    if (noConflictCount > 0) {
      const firstNoConflict = noConflictIndicators.first()
      await expect(firstNoConflict).toHaveClass(/text-green-500/)
    }

    const hasConflictCount = await hasConflictIndicators.count()
    if (hasConflictCount > 0) {
      const firstHasConflict = hasConflictIndicators.first()
      await expect(firstHasConflict).toHaveClass(/text-red-500/)
    }

    // Check CI status indicators
    const passingTests = page.locator('[aria-label="Tests passing"]')
    const failingTests = page.locator('[aria-label="Tests failing"]')
    const runningTests = page.locator('[aria-label="Tests running"]')

    const passingCount = await passingTests.count()
    if (passingCount > 0) {
      const firstPassing = passingTests.first()
      await expect(firstPassing).toHaveClass(/text-green-500/)
    }

    const failingCount = await failingTests.count()
    if (failingCount > 0) {
      const firstFailing = failingTests.first()
      await expect(firstFailing).toHaveClass(/text-red-500/)
    }

    const runningCount = await runningTests.count()
    if (runningCount > 0) {
      const firstRunning = runningTests.first()
      await expect(firstRunning).toHaveClass(/text-yellow-500/)
      await expect(firstRunning).toHaveClass(/animate-spin/)
    }
  })

  test('PR status badges are positioned next to PR link', async ({ page }) => {
    // Wait for task graph to load
    await page.waitForSelector('[role="row"]')

    // Find a row with both PR link and status badges
    const rowsWithPR = await page.locator('[role="row"]').filter({
      has: page.locator('a[href*="/pull/"]'),
    })

    const rowCount = await rowsWithPR.count()
    if (rowCount === 0) {
      test.skip(true, 'No issues with linked PRs found')
      return
    }

    const firstRowWithPR = rowsWithPR.first()
    const prLink = firstRowWithPR.locator('a[href*="/pull/"]')

    // Get the PR link's bounding box
    const prLinkBox = await prLink.boundingBox()
    if (!prLinkBox) {
      throw new Error('Could not get PR link bounding box')
    }

    // Find status indicators
    const statusIndicators = firstRowWithPR.locator(
      'svg[aria-label*="conflicts"], svg[aria-label*="Tests"]'
    )
    const indicatorCount = await statusIndicators.count()

    if (indicatorCount > 0) {
      const firstIndicator = statusIndicators.first()
      const indicatorBox = await firstIndicator.boundingBox()

      if (!indicatorBox) {
        throw new Error('Could not get indicator bounding box')
      }

      // Verify the indicator is close to the PR link (within 50px horizontally)
      const horizontalDistance = Math.abs(indicatorBox.x - (prLinkBox.x + prLinkBox.width))
      expect(horizontalDistance).toBeLessThan(50)

      // Verify they're vertically aligned (similar y position)
      const verticalDifference = Math.abs(indicatorBox.y - prLinkBox.y)
      expect(verticalDifference).toBeLessThan(10)
    }
  })
})
