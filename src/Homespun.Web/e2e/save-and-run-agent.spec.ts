import { test, expect } from '@playwright/test'

test.describe('Save and Run Agent', () => {
  test('saves issue and opens agent launcher dialog', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/sample-project/issues')

    // Find an issue to edit
    const issueCard = page.locator('.rounded-lg').filter({ hasText: 'Feature: Enhance homepage' }).first()
    await expect(issueCard).toBeVisible()

    // Click on the issue to view details
    await issueCard.click()

    // Wait for the detail panel to open
    const detailPanel = page.locator('aside[data-testid="issue-detail-panel"]')
    await expect(detailPanel).toBeVisible()

    // Click the edit button
    const editButton = page.getByRole('button', { name: 'Edit issue' })
    await editButton.click()

    // Wait for navigation to edit page
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)

    // Verify we're on the edit page
    await expect(page.getByText('Edit Issue')).toBeVisible()

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
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Launch Agent' })
    await expect(agentDialog).toBeVisible({ timeout: 10000 })

    // Verify dialog has the prompt selector
    const promptSelector = agentDialog.locator('button[role="combobox"]').first()
    await expect(promptSelector).toBeVisible()
    await expect(promptSelector).toHaveText('Select a prompt')

    // Close the dialog
    const closeButton = agentDialog.getByRole('button', { name: 'Cancel' })
    await closeButton.click()

    // Verify dialog is closed
    await expect(agentDialog).not.toBeVisible()

    // Verify we're still on the edit page (not navigated away)
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)
  })

  test('validates form before save and run', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/sample-project/issues')

    // Find an issue to edit
    const issueCard = page.locator('.rounded-lg').filter({ hasText: 'Feature: Enhance homepage' }).first()
    await expect(issueCard).toBeVisible()

    // Click on the issue
    await issueCard.click()

    // Wait for detail panel and click edit
    const detailPanel = page.locator('aside[data-testid="issue-detail-panel"]')
    await expect(detailPanel).toBeVisible()

    const editButton = page.getByRole('button', { name: 'Edit issue' })
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
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Launch Agent' })
    await expect(agentDialog).not.toBeVisible()
  })

  test('regular save button navigates away', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/sample-project/issues')

    // Find an issue to edit
    const issueCard = page.locator('.rounded-lg').filter({ hasText: 'Feature: Enhance homepage' }).first()
    await expect(issueCard).toBeVisible()

    // Click on the issue
    await issueCard.click()

    // Wait for detail panel and click edit
    const detailPanel = page.locator('aside[data-testid="issue-detail-panel"]')
    await expect(detailPanel).toBeVisible()

    const editButton = page.getByRole('button', { name: 'Edit issue' })
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
    await expect(page).toHaveURL('/projects/sample-project/issues')

    // Verify agent launcher dialog does NOT open
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Launch Agent' })
    await expect(agentDialog).not.toBeVisible()
  })

  test('can start agent from launcher after save and run', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/sample-project/issues')

    // Find an issue to edit
    const issueCard = page.locator('.rounded-lg').filter({ hasText: 'Feature: Enhance homepage' }).first()
    await expect(issueCard).toBeVisible()

    // Click on the issue
    await issueCard.click()

    // Wait for detail panel and click edit
    const detailPanel = page.locator('aside[data-testid="issue-detail-panel"]')
    await expect(detailPanel).toBeVisible()

    const editButton = page.getByRole('button', { name: 'Edit issue' })
    await editButton.click()

    // Wait for navigation to edit page
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)

    // Make a change
    const descriptionTextarea = page.getByLabel('Description')
    await descriptionTextarea.fill('Updated description for testing agent launch')

    // Click Save & Run Agent button
    const saveAndRunButton = page.getByRole('button', { name: 'Save & Run Agent' })
    await saveAndRunButton.click()

    // Wait for agent launcher dialog
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Launch Agent' })
    await expect(agentDialog).toBeVisible({ timeout: 10000 })

    // Select a prompt
    const promptSelector = agentDialog.locator('button[role="combobox"]').first()
    await promptSelector.click()

    // Select first prompt option
    const promptOption = page.getByRole('option').first()
    await promptOption.click()

    // Verify Launch button becomes enabled
    const launchButton = agentDialog.getByRole('button', { name: 'Launch' })
    await expect(launchButton).not.toBeDisabled()

    // Click Launch (in mock mode, this won't actually start an agent)
    await launchButton.click()

    // Dialog should close after launching
    await expect(agentDialog).not.toBeVisible()

    // Should navigate to the agent chat page
    await expect(page).toHaveURL(/\/agents\//)
  })
})