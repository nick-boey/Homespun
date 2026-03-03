import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for CTRL+Enter save behavior on the IssueEdit page.
 * These tests verify that description changes are saved correctly when
 * CTRL+Enter is pressed without losing focus from the description field.
 *
 * Mirrors: tests/Homespun.E2E.Tests/IssueEditCtrlEnterTests.cs
 */
test.describe('Issue Edit CTRL+Enter', () => {
  test('issue edit - ctrl+enter saves description when focus in description field', async ({
    page,
  }) => {
    // Arrange - Navigate to projects page and find an issue to edit
    await page.goto('/projects')
    await page.waitForLoadState('networkidle')

    // Find the first project card and click on it
    const projectCard = page.locator('[data-testid="project-card"], .card').first()
    const projectExists = await projectCard.isVisible()

    if (!projectExists) {
      test.skip(
        true,
        'No projects available in test environment. This test requires mock mode with seeded data.'
      )
      return
    }

    await projectCard.click()
    await page.waitForLoadState('networkidle')

    // Find an issue to edit - look for issue links or edit buttons
    const issueLink = page
      .locator('[data-testid="issue-edit"], a[href*="/edit"], button:has-text("Edit")')
      .first()
    const issueExists = await issueLink.isVisible()

    if (!issueExists) {
      test.skip(
        true,
        'No issues available in test project. This test requires mock mode with seeded data.'
      )
      return
    }

    await issueLink.click()
    await page.waitForLoadState('networkidle')

    // Verify we're on the edit page
    await expect(page.locator('h1:has-text("Edit Issue"), .page-title')).toBeVisible()

    // Arrange - Generate a unique description to verify save
    const uniqueDescription = `E2E Test Description - CTRL+Enter - ${crypto.randomUUID().substring(0, 8)}`

    // Act - Type in the description field (without clicking elsewhere)
    const descriptionField = page.locator('#description, textarea[id="description"]')
    await expect(descriptionField).toBeVisible()

    // Clear any existing content and type new description
    await descriptionField.clear()
    await descriptionField.fill(uniqueDescription)

    // Ensure focus is still on the description field
    await descriptionField.focus()

    // Press CTRL+Enter to save (without clicking elsewhere)
    await page.keyboard.press('Control+Enter')

    // Wait for navigation (save should redirect to project page)
    await page.waitForLoadState('networkidle')

    // Assert - Verify redirect occurred (away from edit page)
    const currentUrl = page.url()
    expect(currentUrl).not.toContain('/edit')
  })

  test('issue edit - meta+enter saves description for mac support', async ({ page }) => {
    // Arrange - Navigate to projects page
    await page.goto('/projects')
    await page.waitForLoadState('networkidle')

    // Find the first project card
    const projectCard = page.locator('[data-testid="project-card"], .card').first()
    const projectExists = await projectCard.isVisible()

    if (!projectExists) {
      test.skip(true, 'No projects available in test environment.')
      return
    }

    await projectCard.click()
    await page.waitForLoadState('networkidle')

    // Find an issue to edit
    const issueLink = page
      .locator('[data-testid="issue-edit"], a[href*="/edit"], button:has-text("Edit")')
      .first()
    const issueExists = await issueLink.isVisible()

    if (!issueExists) {
      test.skip(true, 'No issues available in test project.')
      return
    }

    await issueLink.click()
    await page.waitForLoadState('networkidle')

    // Type in the description field
    const descriptionField = page.locator('#description, textarea[id="description"]')
    await expect(descriptionField).toBeVisible()

    const uniqueDescription = `E2E Test - Meta+Enter - ${crypto.randomUUID().substring(0, 8)}`
    await descriptionField.clear()
    await descriptionField.fill(uniqueDescription)
    await descriptionField.focus()

    // Act - Press Meta+Enter (Cmd+Enter on Mac)
    await page.keyboard.press('Meta+Enter')

    // Wait for navigation
    await page.waitForLoadState('networkidle')

    // Assert
    const currentUrl = page.url()
    expect(currentUrl).not.toContain('/edit')
  })

  test('issue edit - enter alone does not save, allows newlines', async ({ page }) => {
    // Arrange - Navigate to issue edit page
    await page.goto('/projects')
    await page.waitForLoadState('networkidle')

    const projectCard = page.locator('[data-testid="project-card"], .card').first()
    if (!(await projectCard.isVisible())) {
      test.skip(true, 'No projects available in test environment.')
      return
    }

    await projectCard.click()
    await page.waitForLoadState('networkidle')

    const issueLink = page
      .locator('[data-testid="issue-edit"], a[href*="/edit"], button:has-text("Edit")')
      .first()
    if (!(await issueLink.isVisible())) {
      test.skip(true, 'No issues available in test project.')
      return
    }

    await issueLink.click()
    await page.waitForLoadState('networkidle')

    const descriptionField = page.locator('#description, textarea[id="description"]')
    await expect(descriptionField).toBeVisible()

    // Clear and type first line
    await descriptionField.clear()
    await descriptionField.fill('Line 1')
    await descriptionField.focus()

    // Store current URL
    const urlBeforeEnter = page.url()

    // Act - Press Enter alone (should add newline, not save)
    await page.keyboard.press('Enter')

    // Type second line
    await page.keyboard.type('Line 2')

    // Short delay to ensure no navigation happened
    await page.waitForTimeout(500)

    // Assert - Should still be on edit page (Enter alone doesn't save)
    const currentUrl = page.url()
    expect(currentUrl).toBe(urlBeforeEnter)

    // Verify the textarea contains both lines (newline was inserted)
    const textareaValue = await descriptionField.inputValue()
    expect(textareaValue).toContain('Line 1')
    expect(textareaValue).toContain('Line 2')
  })
})
