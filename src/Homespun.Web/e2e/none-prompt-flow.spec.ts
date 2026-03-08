import { test, expect } from '@playwright/test'

test.describe('None Prompt Flow', () => {
  test('selecting None prompt navigates to session', async ({ page }) => {
    // Navigate to projects page
    await page.goto('/projects')

    // Click on the first project
    await page.locator('[data-slot="card"]').first().click()

    // Navigate to issues
    await page.click('text=Issues')

    // Click on the first issue to select it
    await page.locator('[data-testid="issue-row"]').first().click()

    // Click the run agent button
    await page.click('button:has-text("Run Agent")')

    // Wait for the dialog to appear
    await expect(page.getByRole('dialog')).toBeVisible()

    // Open the prompt dropdown
    await page.click('[aria-label="Select prompt"]')

    // Select the "None" option if it exists
    const noneOption = page.locator('text=None - Start without prompt (Plan mode)')

    // Check if None option exists (depends on whether Plan prompts exist)
    const hasNoneOption = (await noneOption.count()) > 0

    if (hasNoneOption) {
      // Click the None option
      await noneOption.click()

      // Click Start Agent
      await page.click('button:has-text("Start Agent")')

      // Verify navigation to session page
      await page.waitForURL(/\/sessions\/[a-zA-Z0-9-]+/)

      // Verify we're on a session page
      await expect(page.getByRole('heading', { name: /Session/ })).toBeVisible()
    } else {
      // If no Plan prompts exist, None option shouldn't be available
      await expect(noneOption).not.toBeVisible()
    }
  })

  test('None option only shows for Plan mode prompts', async ({ page }) => {
    // This test verifies that the None option is conditionally shown
    // It requires setting up specific prompt configurations in mock data

    // Navigate to global prompts to check what types exist
    await page.goto('/prompts')

    // Count Plan mode prompts
    const planBadges = page.locator('text=Plan')
    const planCount = await planBadges.count()

    // Navigate to a project issue
    await page.goto('/projects')
    await page.locator('[data-slot="card"]').first().click()
    await page.click('text=Issues')
    await page.locator('[data-testid="issue-row"]').first().click()
    await page.click('button:has-text("Run Agent")')

    // Open prompt dropdown
    await page.click('[aria-label="Select prompt"]')

    // Check None option visibility
    const noneOption = page.locator('text=None - Start without prompt (Plan mode)')

    if (planCount > 0) {
      // If Plan prompts exist, None should be available
      await expect(noneOption).toBeVisible()
    } else {
      // If no Plan prompts exist, None should not be available
      await expect(noneOption).not.toBeVisible()
    }
  })
})
