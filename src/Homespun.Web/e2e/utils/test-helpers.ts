import { Page, Locator, APIRequestContext } from '@playwright/test'

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
 * Create a mock agent session via API and optionally send a message to generate tool use messages.
 * Returns the session ID.
 */
export async function createMockSession(
  request: APIRequestContext,
  options: { entityId?: string; sendMessage?: string } = {}
): Promise<string> {
  const { entityId = 'ISSUE-003', sendMessage } = options

  // Create session via POST /api/sessions
  const createResponse = await request.post('/api/sessions', {
    data: {
      entityId,
      projectId: 'demo-project',
      mode: 'build',
      model: 'sonnet',
    },
  })

  const session = await createResponse.json()
  const sessionId = session.id

  if (sendMessage) {
    // Send a message to generate tool use responses
    await request.post(`/api/sessions/${sessionId}/messages`, {
      data: { message: sendMessage },
    })
    // Wait for the mock service to process the message
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }

  return sessionId
}

/**
 * Clear any active issue filter so all issues are visible.
 * Checks if the My Tasks button is active (aria-pressed) and toggles it off,
 * or clears the filter input if the filter panel is open.
 */
export async function clearIssueFilter(page: Page) {
  // Check if My Tasks button is active and toggle it off
  const myTasksButton = page.locator('[data-testid="toolbar-my-tasks-button"]')
  const isMyTasksActive = await myTasksButton.getAttribute('aria-pressed')
  if (isMyTasksActive === 'true') {
    await myTasksButton.click()
    await page.waitForTimeout(500)
    return
  }

  // Check if filter panel is open
  const filterInput = page.locator('[data-testid="filter-input"]')
  if (await filterInput.isVisible({ timeout: 1000 }).catch(() => false)) {
    // Clear the filter input
    await filterInput.click({ clickCount: 3 })
    await page.keyboard.press('Backspace')
    await filterInput.press('Enter')
    await page.waitForTimeout(500)
  }
}
