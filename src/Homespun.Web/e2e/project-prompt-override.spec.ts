import { test, expect } from '@playwright/test'

test.describe('Project Prompt Override', () => {
  test('editing a global prompt creates an override that remains visible', async ({ page }) => {
    // Navigate to project prompts page
    await page.goto('/projects/demo-project/prompts')
    await page.waitForLoadState('networkidle')

    // Verify inherited global prompts section is visible
    await expect(page.getByRole('heading', { name: 'Inherited Global Prompts' })).toBeVisible()

    // Get the name of the first inherited global prompt card
    const inheritedCards = page
      .getByRole('heading', { name: 'Inherited Global Prompts' })
      .locator('~ div')
      .locator('[data-slot="card"]')
    const firstGlobalCard = inheritedCards.first()
    await expect(firstGlobalCard).toBeVisible()
    const promptName = await firstGlobalCard.locator('[data-slot="card-title"]').textContent()
    const cleanName = promptName!.trim()

    // Open the Actions dropdown on the first card and click Edit
    await firstGlobalCard.getByRole('button', { name: 'Actions' }).click()
    await page.getByRole('menuitem', { name: 'Edit' }).click()

    // The form should show "Create Project Override" heading
    await expect(page.getByRole('heading', { name: 'Create Project Override' })).toBeVisible()

    // Modify the initial message
    const textarea = page.getByRole('textbox', { name: 'System Prompt' })
    await textarea.clear()
    await textarea.fill('Overridden prompt message for project')

    // Submit the form
    await page.getByRole('button', { name: 'Update Prompt' }).click()

    // Wait for data to refresh and return to the prompts list
    await page.waitForLoadState('networkidle')

    // Verify the prompt now appears under "Project Prompts" section with "(project)" label
    await expect(page.getByRole('heading', { name: 'Project Prompts' })).toBeVisible()

    // The override card should appear in the Project Prompts section with (project) label
    const projectCards = page
      .getByRole('heading', { name: 'Project Prompts' })
      .locator('~ div')
      .locator('[data-slot="card"]')
    const overrideCard = projectCards.filter({ hasText: cleanName }).first()
    await expect(overrideCard).toBeVisible()
    await expect(overrideCard.getByText('(project)')).toBeVisible()
  })

  test('removing an override reverts prompt to inherited global', async ({ page }) => {
    // First, create an override so we have one to remove
    await page.goto('/projects/demo-project/prompts')
    await page.waitForLoadState('networkidle')

    // Find and edit the last inherited global prompt to create an override
    const globalCards = page
      .getByRole('heading', { name: 'Inherited Global Prompts' })
      .locator('~ div')
      .locator('[data-slot="card"]')
    const lastGlobalCard = globalCards.last()
    await expect(lastGlobalCard).toBeVisible()
    const promptName = await lastGlobalCard.locator('[data-slot="card-title"]').textContent()
    const cleanName = promptName!.trim()

    // Open Actions dropdown and click Edit
    await lastGlobalCard.getByRole('button', { name: 'Actions' }).click()
    await page.getByRole('menuitem', { name: 'Edit' }).click()

    // The form should show "Create Project Override" heading
    await expect(page.getByRole('heading', { name: 'Create Project Override' })).toBeVisible()

    const textarea = page.getByRole('textbox', { name: 'System Prompt' })
    await textarea.clear()
    await textarea.fill('Temporary override message')
    await page.getByRole('button', { name: 'Update Prompt' }).click()

    // Wait for data to refresh
    await page.waitForLoadState('networkidle')

    // Now the prompt should be in "Project Prompts" section
    await expect(page.getByRole('heading', { name: 'Project Prompts' })).toBeVisible()

    // Find the override card in the Project Prompts section
    const projectCards = page
      .getByRole('heading', { name: 'Project Prompts' })
      .locator('~ div')
      .locator('[data-slot="card"]')
    const overrideCard = projectCards.filter({ hasText: cleanName }).first()
    await expect(overrideCard).toBeVisible()

    // Open the Actions dropdown and click "Remove override"
    await overrideCard.getByRole('button', { name: 'Actions' }).click()
    await page.getByRole('menuitem', { name: 'Remove override' }).click()

    // Confirm the removal in the alert dialog
    await expect(page.getByText('revert to the global prompt')).toBeVisible()
    await page.getByRole('button', { name: 'Remove' }).click()

    // Wait for refresh
    await page.waitForLoadState('networkidle')

    // The prompt should now be back under "Inherited Global Prompts"
    const inheritedCards = page
      .getByRole('heading', { name: 'Inherited Global Prompts' })
      .locator('~ div')
      .locator('[data-slot="card"]')
    await expect(inheritedCards.filter({ hasText: cleanName }).first()).toBeVisible()
  })
})
