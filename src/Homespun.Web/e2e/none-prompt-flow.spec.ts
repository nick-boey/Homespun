import { test, expect } from '@playwright/test'

test.describe('None Prompt Flow', () => {
  test('None option is always available in agent launcher dialog', async ({ page }) => {
    // Navigate to projects page
    await page.goto('/projects')

    // Click on the first project
    await page.locator('[data-testid="project-card-link"]').first().click()

    // Click on the first issue to select it
    await page.locator('[data-testid="task-graph-issue-row"]').first().click()

    // Click the run agent button
    await page.click('[data-testid="toolbar-run-agent"]')

    // Wait for the dialog to appear
    await expect(page.getByRole('dialog')).toBeVisible()

    // Open the prompt dropdown
    await page.click('[aria-label="Select prompt"]')

    // None option should always be visible and first
    const noneOption = page.locator('text=None - Start without prompt (Plan mode)')
    await expect(noneOption).toBeVisible()

    // Verify it's the first option
    const firstOption = page.getByRole('option').first()
    await expect(firstOption).toHaveText('None - Start without prompt (Plan mode)')
  })

  test('selecting None prompt navigates to session page', async ({ page }) => {
    // Navigate to projects page
    await page.goto('/projects')

    // Click on the first project
    await page.locator('[data-testid="project-card-link"]').first().click()

    // Click on the first issue to select it
    await page.locator('[data-testid="task-graph-issue-row"]').first().click()

    // Click the run agent button
    await page.click('[data-testid="toolbar-run-agent"]')

    // Wait for the dialog to appear
    await expect(page.getByRole('dialog')).toBeVisible()

    // Open the prompt dropdown
    await page.click('[aria-label="Select prompt"]')

    // Click the None option
    await page.locator('text=None - Start without prompt (Plan mode)').click()

    // Click Start Agent
    await page.click('button:has-text("Start Agent")')

    // Verify navigation to session page
    await page.waitForURL(/\/sessions\/[a-zA-Z0-9-]+/)

    // Verify we're on a session page
    await expect(page.getByRole('heading', { name: /Session/ })).toBeVisible()

    // Verify prompt input is available
    await expect(page.getByPlaceholder(/Type a message/i)).toBeVisible()
  })

  test('can add custom prompt after starting with None', async ({ page }) => {
    // Navigate to projects page
    await page.goto('/projects')

    // Click on the first project
    await page.locator('[data-testid="project-card-link"]').first().click()

    // Click on the first issue to select it
    await page.locator('[data-testid="task-graph-issue-row"]').first().click()

    // Click the run agent button
    await page.click('[data-testid="toolbar-run-agent"]')

    // Wait for the dialog to appear
    await expect(page.getByRole('dialog')).toBeVisible()

    // Open the prompt dropdown and select None
    await page.click('[aria-label="Select prompt"]')
    await page.locator('text=None - Start without prompt (Plan mode)').click()

    // Start the agent
    await page.click('button:has-text("Start Agent")')

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
