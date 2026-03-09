import { test, expect } from '@playwright/test'

test.describe.serial('Save and Run Agent', () => {
  test('saves issue and opens agent launcher dialog', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/demo-project/issues')

    // Wait for the issues list to load
    await page.waitForLoadState('networkidle')

    // Find the first Feature issue row - specifically ISSUE-001 (Add dark mode support)
    const issueRow = page
      .locator('[role="row"]')
      .filter({ hasText: 'Add dark mode support' })
      .first()
    await expect(issueRow).toBeVisible()

    // Click the Edit button within this row - look for button with aria-label "Edit"
    const editButton = issueRow.locator('button[aria-label="Edit"]')
    await editButton.click()

    // Wait for navigation to edit page
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)

    // Verify we're on the edit page by checking the heading
    await expect(page.getByRole('heading', { name: 'Edit Issue' })).toBeVisible()

    // Wait for form to load
    const titleInput = page.getByLabel('Title')
    await expect(titleInput).toBeVisible()

    // Make a change to the issue
    await titleInput.fill('Feature: Enhanced homepage with new design')

    // Find and click the Save & Run Agent button
    const saveAndRunButton = page.getByRole('button', { name: 'Save & Run Agent' })
    await expect(saveAndRunButton).toBeVisible()
    await saveAndRunButton.click()

    // Wait for save to complete and agent launcher dialog to open
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Run Agent' })
    await expect(agentDialog).toBeVisible({ timeout: 10000 })

    // Verify dialog has the prompt selector
    const promptSelector = agentDialog.locator('button[role="combobox"]').first()
    await expect(promptSelector).toBeVisible()
    // The prompt selector may have a default value, so just check it exists

    // Close the dialog
    const closeButton = agentDialog.getByRole('button', { name: 'Close' })
    await closeButton.click()

    // Verify dialog is closed
    await expect(agentDialog).not.toBeVisible()

    // Verify we're still on the edit page (not navigated away)
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)
  })

  test('validates form before save and run', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/demo-project/issues')

    // Wait for the issues list to load
    await page.waitForLoadState('networkidle')

    // Find a different issue to avoid conflicts - use ISSUE-002 (Improve mobile responsiveness)
    const issueRow = page
      .locator('[role="row"]')
      .filter({ hasText: 'Improve mobile responsiveness' })
      .first()
    await expect(issueRow).toBeVisible()

    // Click the Edit button within this row - look for button with aria-label "Edit"
    const editButton = issueRow.locator('button[aria-label="Edit"]')
    await editButton.click()

    // Wait for navigation to edit page
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)

    // Clear the title to make form invalid
    const titleInput = page.getByLabel('Title')
    await titleInput.clear()

    // Click Save & Run Agent button
    const saveAndRunButton = page.getByRole('button', { name: 'Save & Run Agent' })
    await saveAndRunButton.click()

    // Verify validation error appears
    await expect(page.getByText('Title is required')).toBeVisible()

    // Verify agent launcher dialog does NOT open
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Run Agent' })
    await expect(agentDialog).not.toBeVisible()
  })

  test('regular save button navigates away', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/demo-project/issues')

    // Wait for the issues list to load
    await page.waitForLoadState('networkidle')

    // Find a Bug issue to avoid conflicts - use ISSUE-003 (Fix login timeout bug)
    const issueRow = page
      .locator('[role="row"]')
      .filter({ hasText: 'Fix login timeout bug' })
      .first()
    await expect(issueRow).toBeVisible()

    // Click the Edit button within this row - look for button with aria-label "Edit"
    const editButton = issueRow.locator('button[aria-label="Edit"]')
    await editButton.click()

    // Wait for navigation to edit page
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)

    // Make a change
    const titleInput = page.getByLabel('Title')
    await titleInput.fill('Feature: Updated homepage design')

    // Click regular Save button
    const saveButton = page.getByRole('button', { name: 'Save Changes' })
    await saveButton.click()

    // Wait for navigation back to issues list
    await expect(page).toHaveURL('/projects/demo-project/issues')

    // Verify agent launcher dialog does NOT open
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Run Agent' })
    await expect(agentDialog).not.toBeVisible()
  })

  test('can start agent from launcher after save and run', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/demo-project/issues')

    // Wait for the issues list to load
    await page.waitForLoadState('networkidle')

    // Find a different Task issue - use ISSUE-004 (Design API schema)
    const issueRow = page
      .locator('[role="row"]')
      .filter({ hasText: 'Design API schema' })
      .first()
    await expect(issueRow).toBeVisible()

    // Click the Edit button within this row - look for button with aria-label "Edit"
    const editButton = issueRow.locator('button[aria-label="Edit"]')
    await editButton.click()

    // Wait for navigation to edit page
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)

    // Wait for the form to load
    await page.waitForLoadState('networkidle')

    // Verify we're on the edit page
    await expect(page.getByRole('heading', { name: 'Edit Issue' })).toBeVisible()

    // Make a change
    const descriptionTextarea = page.getByLabel('Description')
    await expect(descriptionTextarea).toBeVisible({ timeout: 10000 })
    await descriptionTextarea.fill('Updated description for testing agent launch')

    // Click Save & Run Agent button
    const saveAndRunButton = page.getByRole('button', { name: 'Save & Run Agent' })
    await saveAndRunButton.click()

    // Wait for agent launcher dialog
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Run Agent' })
    await expect(agentDialog).toBeVisible({ timeout: 10000 })

    // Select a prompt
    const promptSelector = agentDialog.locator('button[role="combobox"]').first()
    await promptSelector.click()

    // Select first prompt option
    const promptOption = page.getByRole('option').first()
    await promptOption.click()

    // Verify Start Agent button becomes enabled
    const startButton = agentDialog.getByRole('button', { name: 'Start Agent' })
    await expect(startButton).not.toBeDisabled()

    // Click Start Agent (in mock mode, this won't actually start an agent)
    await startButton.click()

    // Dialog should close after launching
    await expect(agentDialog).not.toBeVisible()

    // Should navigate to the agent chat page
    await expect(page).toHaveURL(/\/agents\//)
  })
})
