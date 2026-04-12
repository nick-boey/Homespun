import { test, expect } from '@playwright/test'

test.describe('Sessions page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to sessions page
    await page.goto('/sessions')
  })

  test('displays active and archived tabs', async ({ page }) => {
    // Check for tab navigation
    const activeTab = page.getByRole('tab', { name: /active/i })
    const archivedTab = page.getByRole('tab', { name: /archived/i })

    await expect(activeTab).toBeVisible()
    await expect(archivedTab).toBeVisible()

    // Active tab should be selected by default
    await expect(activeTab).toHaveAttribute('aria-selected', 'true')
    await expect(archivedTab).toHaveAttribute('aria-selected', 'false')
  })

  test('shows empty state when no sessions exist', async ({ page }) => {
    // Mock the sessions API to return empty list
    await page.route('**/api/sessions', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    )

    await page.goto('/sessions')

    // Check for empty state message
    await expect(page.getByText('No sessions yet')).toBeVisible()
  })
})
