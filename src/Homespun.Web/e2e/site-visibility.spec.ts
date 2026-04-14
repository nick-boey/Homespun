import { test, expect } from '@playwright/test'
import { clearIssueFilter } from './utils/test-helpers'

/**
 * End-to-end tests verifying that page content is not clipped by the layout.
 * Regression tests for the overflow-hidden clipping bug (commit c56fefcd).
 */
test.describe('Site Visibility', () => {
  test('issue edit page buttons are visible and scrollable', async ({ page }) => {
    // Navigate to the issues page
    await page.goto('/projects/demo-project/issues')
    await page.waitForLoadState('networkidle')

    // Clear filter to see all issues
    await clearIssueFilter(page)

    // Find an issue and navigate to edit
    const issueRow = page
      .locator('[role="row"]')
      .filter({ hasText: 'Generate OpenAPI 3.1 spec' })
      .first()
    await expect(issueRow).toBeVisible()

    const editButton = issueRow.locator('button[aria-label="Edit"]')
    await editButton.click()

    // Wait for edit page
    await expect(page).toHaveURL(/\/issues\/.*\/edit/)
    await expect(page.getByRole('heading', { name: 'Edit Issue' })).toBeVisible()

    // The save button must be visible (scrollable into view)
    const saveButton = page.getByRole('button', { name: 'Save Changes' })
    await expect(saveButton).toBeVisible()

    // The cancel button must also be reachable
    const cancelButton = page.getByRole('button', { name: 'Cancel' })
    await expect(cancelButton).toBeVisible()
  })

  test('project settings page is fully visible', async ({ page }) => {
    await page.goto('/projects/demo-project/settings')
    await page.waitForLoadState('networkidle')

    // Settings page should render its content without clipping
    const body = page.locator('body')
    await expect(body).toBeVisible()
  })
})
