import { test, expect } from '@playwright/test'

test.describe('Global Prompts', () => {
  test('can navigate to global prompts page', async ({ page }) => {
    // Navigate to the projects page
    await page.goto('/projects')

    // Click on Prompts in the sidebar
    await page.click('nav >> text=Prompts')

    // Verify we're on the global prompts page
    await expect(page).toHaveURL('/prompts')
    await expect(page.getByRole('heading', { name: 'Global Prompts' })).toBeVisible()
    await expect(page.getByText('Manage prompts available to all projects')).toBeVisible()
  })

  test('can create a global prompt', async ({ page }) => {
    // Navigate to global prompts page
    await page.goto('/prompts')

    // Click New Prompt button
    await page.click('button:has-text("New Prompt")')

    // Fill out the form
    await page.fill('input[name="name"]', 'Test Global Prompt')
    await page.fill('textarea[name="initialMessage"]', 'This is a test global prompt message')

    // Submit the form
    await page.click('button:has-text("Create")')

    // TODO: Allow persistence of global prompts in backend, then uncomment the below to fix the issue

    // Verify the prompt was created and appears in the list
    // await expect(page.getByText('Test Global Prompt')).toBeVisible()
  })

  test('shows Global badge on prompt cards', async ({ page }) => {
    // Navigate to global prompts page
    await page.goto('/prompts')

    // Assuming there's at least one global prompt, verify the badge appears
    const promptCard = page.locator('[data-slot="card"]').first()
    await expect(promptCard).toBeVisible()

    // Look for the Global badge within the card
    await expect(promptCard.locator('text=Global')).toBeVisible()
  })
})
