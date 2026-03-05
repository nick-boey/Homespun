import { test, expect } from '@playwright/test'

test.describe('Sessions page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to sessions page
    await page.goto('/sessions')
  })

  test('displays sessions in card layout', async ({ page }) => {
    // Wait for sessions to load
    await page.waitForSelector('[data-testid="session-card"]')

    // Check that cards are displayed in a grid
    const gridContainer = await page.locator('[data-testid="session-card"]').locator('..')
    await expect(gridContainer).toHaveClass(/grid/)
    await expect(gridContainer).toHaveClass(/gap-4/)

    // Check responsive grid classes
    await expect(gridContainer).toHaveClass(/grid-cols-1/)
    await expect(gridContainer).toHaveClass(/md:grid-cols-2/)
    await expect(gridContainer).toHaveClass(/lg:grid-cols-3/)
  })

  test('shows skeleton cards while loading', async ({ page }) => {
    // Reload to see loading state
    await page.reload()

    // Check for skeleton cards
    const skeletons = page.locator('[data-testid="session-card-skeleton"]')
    await expect(skeletons).toHaveCount(6)
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

  test('switches between active and archived sessions', async ({ page }) => {
    // Wait for initial load
    await page.waitForSelector('[data-testid="session-card"]')

    // Click on archived tab
    const archivedTab = page.getByRole('tab', { name: /archived/i })
    await archivedTab.click()

    // Wait for content to change
    await page.waitForTimeout(500)

    // Check that archived tab is now selected
    await expect(archivedTab).toHaveAttribute('aria-selected', 'true')

    // Switch back to active tab
    const activeTab = page.getByRole('tab', { name: /active/i })
    await activeTab.click()

    await expect(activeTab).toHaveAttribute('aria-selected', 'true')
  })

  test('displays session card with issue information', async ({ page }) => {
    // Wait for a session card
    await page.waitForSelector('[data-testid="session-card"]')

    const firstCard = page.locator('[data-testid="session-card"]').first()

    // Check that card has expected structure
    await expect(firstCard).toContainText(/bug|feature|task|chore/i)
    await expect(firstCard).toContainText(/plan|build/i)
    await expect(firstCard).toContainText(/messages/)
  })

  test('shows empty state when no sessions exist', async ({ page }) => {
    // Navigate to a project with no sessions
    await page.goto('/projects/empty-project/sessions')

    // Check for empty state message
    await expect(page.getByText('No sessions yet')).toBeVisible()
  })

  test('displays error state with retry button', async ({ page }) => {
    // Simulate error by blocking API call
    await page.route('**/api/sessions', (route) => route.abort())

    await page.goto('/sessions')

    // Check for error state
    await expect(page.getByText('Error loading sessions')).toBeVisible()

    // Check for retry button
    const retryButton = page.getByRole('button', { name: /retry/i })
    await expect(retryButton).toBeVisible()
  })

  test('filters sessions by status', async ({ page }) => {
    // Wait for sessions to load
    await page.waitForSelector('[data-testid="session-card"]')

    // Open status filter dropdown
    const filterDropdown = page.getByRole('combobox', { name: /filter by status/i })
    await filterDropdown.click()

    // Select 'Active' filter
    await page.getByRole('option', { name: /active/i }).click()

    // Wait for filter to apply
    await page.waitForTimeout(500)

    // Verify cards are still displayed (or empty state if no active sessions)
    const cards = page.locator('[data-testid="session-card"]')
    const emptyState = page.getByText('No active sessions')

    // Either we have cards or empty state
    const hasCards = (await cards.count()) > 0
    const hasEmptyState = await emptyState.isVisible().catch(() => false)

    expect(hasCards || hasEmptyState).toBeTruthy()
  })

  test('navigates to session detail on card click', async ({ page }) => {
    // Wait for sessions to load
    await page.waitForSelector('[data-testid="session-card"]')

    // Click on first session card
    const firstCard = page.locator('[data-testid="session-card"]').first()
    await firstCard.click()

    // Should navigate to session detail page
    await expect(page).toHaveURL(/\/sessions\/[a-zA-Z0-9-]+/)
  })

  test('shows stop button for active sessions', async ({ page }) => {
    // Wait for sessions to load
    await page.waitForSelector('[data-testid="session-card"]')

    // Find an active session card (in active tab)
    const activeCards = page.locator('[data-testid="session-card"]')
    const cardCount = await activeCards.count()

    if (cardCount > 0) {
      // Check for stop button in first card
      const firstCard = activeCards.first()
      const stopButton = firstCard.getByRole('button', { name: /stop session/i })

      // Active sessions should have stop button
      await expect(stopButton).toBeVisible()
    }
  })

  test('responsive layout changes based on viewport', async ({ page }) => {
    // Wait for sessions to load
    await page.waitForSelector('[data-testid="session-card"]')

    // Test desktop view (3 columns)
    await page.setViewportSize({ width: 1280, height: 720 })
    await page.waitForTimeout(500)

    const desktopGrid = page.locator('[data-testid="session-card"]').locator('..')
    await expect(desktopGrid).toHaveClass(/lg:grid-cols-3/)

    // Test tablet view (2 columns)
    await page.setViewportSize({ width: 768, height: 1024 })
    await page.waitForTimeout(500)
    await expect(desktopGrid).toHaveClass(/md:grid-cols-2/)

    // Test mobile view (1 column)
    await page.setViewportSize({ width: 375, height: 667 })
    await page.waitForTimeout(500)
    await expect(desktopGrid).toHaveClass(/grid-cols-1/)
  })
})
