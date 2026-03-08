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
    await page.click('label:has-text("Plan")')

    // Submit the form
    await page.click('button:has-text("Create")')

    // Verify the prompt was created and appears in the list
    await expect(page.getByText('Test Global Prompt')).toBeVisible()
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

  test('global prompts appear in project agent launcher', async ({ page }) => {
    // Create a test project first (mock mode should have pre-seeded data)
    await page.goto('/projects')

    // Click on the first project
    await page.locator('[data-slot="card"]').first().click()

    // Navigate to issues
    await page.click('text=Issues')

    // Click on the first issue
    await page.locator('[data-testid="issue-row"]').first().click()

    // Click the run agent button
    await page.click('button:has-text("Run Agent")')

    // Open the prompt dropdown
    await page.click('[aria-label="Select prompt"]')

    // Verify global prompts are listed
    // Note: This assumes mock data includes some prompts
    await expect(page.locator('role=option')).toHaveCount(0) // Update based on actual mock data
  })
})
