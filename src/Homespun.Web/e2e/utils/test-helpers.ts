import { Page, Locator } from '@playwright/test'

/**
 * Navigate to a URL and wait for the page to be fully loaded
 */
export async function navigateAndWait(page: Page, url: string) {
  await page.goto(url)
  await page.waitForLoadState('networkidle')
}

/**
 * Wait for the agent status indicator to load (may or may not be visible depending on active sessions)
 */
export async function waitForStatusIndicator(page: Page) {
  // Wait a moment for the API call to complete
  await page.waitForTimeout(500)
}

/**
 * Safely check if an element is visible without throwing errors
 * Returns false if the element is not visible within the timeout
 */
export async function isElementSafelyVisible(locator: Locator, timeout = 1000): Promise<boolean> {
  try {
    return await locator.isVisible({ timeout })
  } catch {
    return false
  }
}

/**
 * Wait for CSS transitions to complete after viewport changes
 */
export async function waitForCssTransition(page: Page, duration = 100) {
  await page.waitForTimeout(duration)
}

/**
 * Create a test project via API
 */
export async function createTestProject(page: Page, projectId: string) {
  return await page.request.post('/api/projects', {
    data: {
      id: projectId,
      name: 'Test Project',
      description: 'Test project for E2E',
      owner: 'test-owner',
      repository: 'test-repo',
    },
  })
}

/**
 * Check if any of the status indicators are visible
 */
export async function checkStatusIndicators(page: Page) {
  const statuses = ['working', 'waiting-input', 'waiting-answer', 'waiting-plan', 'error']
  const visibleStatuses: string[] = []

  for (const status of statuses) {
    const indicator = page.locator(`[data-testid="status-${status}"]`)
    if (await isElementSafelyVisible(indicator)) {
      visibleStatuses.push(status)
    }
  }

  return visibleStatuses
}

/**
 * Wait for the agent status to stabilize (no more polling updates)
 */
export async function waitForStableStatus(page: Page, stabilityDuration = 6000) {
  // The status indicator polls every 5 seconds, so wait for at least one full cycle
  await page.waitForTimeout(stabilityDuration)
}

/**
 * Clear the filter on the issues page so all issues are visible.
 * This is needed because the page now has a default filter applied.
 */
export async function clearIssueFilter(page: Page) {
  // Wait for the filter input to be visible (it's shown by default now)
  const filterInput = page.locator('[data-testid="filter-input"]')

  // Wait up to 5 seconds for filter input to appear
  try {
    await filterInput.waitFor({ state: 'visible', timeout: 5000 })
  } catch {
    // Filter panel might not be visible, press 'f' to open it
    await page.keyboard.press('f')
    await page.waitForTimeout(500)
  }

  // Now clear the filter - triple-click to select all and then type empty
  await filterInput.click({ clickCount: 3 })
  await page.keyboard.press('Backspace')
  await filterInput.press('Enter')

  // Wait for issues to load after filter is cleared
  await page.waitForTimeout(500)
}
