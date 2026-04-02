import { test, expect } from '@playwright/test'
import { clearIssueFilter } from './utils/test-helpers'

test.describe('None Prompt Flow', () => {
  // Each test uses a different issue to avoid conflicts from concurrent agent prevention
  test('None option is always available in agent launcher dialog', async ({ page }) => {
    // Navigate to projects page
    await page.goto('/projects')

    // Click on the first project
    await page.locator('[data-testid="project-card-link"]').first().click()

    // Wait for page to load and clear the default filter to show all issues
    await page.waitForLoadState('networkidle')
    await clearIssueFilter(page)

    // Select "Add dark mode support" issue to avoid conflicts with other tests
    await page
      .locator('[data-testid="task-graph-issue-row"]')
      .filter({ hasText: 'Add dark mode support' })
      .first()
      .click()

    // Click the run agent button
    await page.click('[data-testid="toolbar-run-agent"]')

    // Wait for the dialog to appear
    await expect(page.getByRole('dialog')).toBeVisible()

    // Open the prompt dropdown
    await page.click('[aria-label="Select prompt"]')

    // None option should always be visible and first
    const noneOption = page.locator('text=None - Start without prompt')
    await expect(noneOption).toBeVisible()

    // Verify it's the first option
    const firstOption = page.getByRole('option').first()
    await expect(firstOption).toHaveText('None - Start without prompt')
  })

  // Skip: This test needs to be updated for async agent starting behavior
  // The dialog no longer closes immediately when clicking Start Agent in E2E tests
  // because the row click opens a status dropdown instead of selecting the row
  test.skip('selecting None prompt starts agent asynchronously', async ({ page }) => {
    // Navigate to projects page
    await page.goto('/projects')

    // Click on the first project
    await page.locator('[data-testid="project-card-link"]').first().click()

    // Wait for page to load and clear the default filter to show all issues
    await page.waitForLoadState('networkidle')
    await clearIssueFilter(page)

    // Select "E2E Test: Orphan Issue" issue - created specifically for E2E tests
    await page
      .locator('[data-testid="task-graph-issue-row"]')
      .filter({ hasText: 'E2E Test: Orphan Issue' })
      .first()
      .click()

    // Click the run agent button
    await page.click('[data-testid="toolbar-run-agent"]')

    // Wait for the dialog to appear
    await expect(page.getByRole('dialog')).toBeVisible()

    // Open the prompt dropdown
    await page.click('[aria-label="Select prompt"]')

    // Click the None option
    await page.locator('text=None - Start without prompt').click()

    // Click Start Agent
    await page.click('button:has-text("Start Agent")')

    // With async agent starting, the dialog closes immediately
    // and the session is created in the background
    await expect(page.getByRole('dialog')).not.toBeVisible()

    // With async agent starting, the dialog closes immediately
    // The session is created in the background and we stay on the issues page
    // Verify we're still on the issues page (not navigated away)
    await expect(page).toHaveURL(/\/projects\/[^/]+\/issues/)
  })

  // Skip: This test needs to be updated for async agent starting behavior
  test.skip('can add custom prompt after starting with None', async ({ page }) => {
    // Navigate to projects page
    await page.goto('/projects')

    // Click on the first project
    await page.locator('[data-testid="project-card-link"]').first().click()

    // Wait for page to load and clear the default filter to show all issues
    await page.waitForLoadState('networkidle')
    await clearIssueFilter(page)

    // Select "E2E Test: Series Parent" issue - created specifically for E2E tests
    await page
      .locator('[data-testid="task-graph-issue-row"]')
      .filter({ hasText: 'E2E Test: Series Parent' })
      .first()
      .click()

    // Click the run agent button
    await page.click('[data-testid="toolbar-run-agent"]')

    // Wait for the dialog to appear
    await expect(page.getByRole('dialog')).toBeVisible()

    // Open the prompt dropdown and select None
    await page.click('[aria-label="Select prompt"]')
    await page.locator('text=None - Start without prompt').click()

    // Start the agent
    await page.click('button:has-text("Start Agent")')

    // With async agent starting, the dialog closes immediately
    await expect(page.getByRole('dialog')).not.toBeVisible()

    // With async agent starting, we stay on the issues page
    // The session is created in the background and SignalR events update the header
    // Verify we're still on the issues page
    await expect(page).toHaveURL(/\/projects\/[^/]+\/issues/)

    // Wait for the session to be created in the background
    await page.waitForTimeout(1000)

    // Navigate to sessions page to find and open the session
    await page.goto('/sessions')
    await page.waitForLoadState('networkidle')

    // Wait for the page to show Active sessions tab content
    // The sessions list uses a Link component to navigate to each session
    const sessionLink = page.locator('a[href^="/sessions/"]').first()
    await expect(sessionLink).toBeVisible({ timeout: 10000 })
    await sessionLink.click()

    // Wait for navigation to session page
    await page.waitForURL(/\/sessions\/[a-zA-Z0-9-]+/)

    // Type a custom prompt
    const promptInput = page.getByPlaceholder(/Type a message/i)
    await promptInput.fill('Please analyze the issue and create a plan to fix it.')

    // Submit the prompt
    await promptInput.press('Enter')

    // Verify the prompt appears in the conversation
    await expect(
      page.getByText('Please analyze the issue and create a plan to fix it.')
    ).toBeVisible()
  })
})
