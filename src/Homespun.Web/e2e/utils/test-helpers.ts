import { Page, Locator } from '@playwright/test'

/**
 * Navigate to a URL and wait for the page to be fully loaded
 */
export async function navigateAndWait(page: Page, url: string) {
  await page.goto(url)
  await page.waitForLoadState('networkidle')
}

/**
 * Wait for the agent status indicator to be visible and its initial API call to complete
 */
export async function waitForStatusIndicator(page: Page) {
  await page.waitForSelector('[data-testid="status-indicator"]', { state: 'visible' })

  // Wait for initial API call to complete
  await page.waitForResponse(
    (response) => response.url().includes('/api/sessions') && response.status() === 200,
    { timeout: 10000 }
  )
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
