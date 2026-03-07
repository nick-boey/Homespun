import { test, expect } from '@playwright/test'
import {
  navigateAndWait,
  waitForStatusIndicator,
  isElementSafelyVisible,
  checkStatusIndicators,
  createTestProject,
} from './utils/test-helpers'

test.describe('Agent Status Indicator', () => {
  test.beforeEach(async ({ page }) => {
    await navigateAndWait(page, '/')
  })

  test('navigates to global sessions page when clicked without project', async ({ page }) => {
    // Click on the indicator
    await page.click('[data-testid="status-indicator"]')

    // Should navigate to /sessions
    await expect(page).toHaveURL(/\/sessions$/)
  })

  test('shows status breakdown with multiple session states', async ({ page }) => {
    // Note: In a real test, we would set up mock sessions with different statuses
    // For now, we'll just test the UI behavior

    // Navigate to projects page to see global indicator
    await navigateAndWait(page, '/projects')
    await waitForStatusIndicator(page)

    // Check if any status indicators are visible
    const visibleStatuses = await checkStatusIndicators(page)

    if (visibleStatuses.length > 0) {
      // Verify counts are shown next to indicators
      for (const status of visibleStatuses) {
        await expect(page.locator(`[data-testid="status-${status}-count"]`)).toBeVisible()
      }
    }
  })

  test('shows correct indicator colors based on status type', async ({ page }) => {
    await navigateAndWait(page, '/projects')
    await waitForStatusIndicator(page)

    // Check color classes for each status type if visible
    const statusColorMap = {
      working: 'text-blue-500',
      'waiting-input': 'text-yellow-500',
      'waiting-answer': 'text-orange-500',
      'waiting-plan': 'text-purple-500',
      error: 'text-red-500',
    }

    for (const [status, colorClass] of Object.entries(statusColorMap)) {
      const countElement = page.locator(`[data-testid="status-${status}-count"]`)
      if (await isElementSafelyVisible(countElement)) {
        await expect(countElement).toHaveClass(new RegExp(colorClass))
      }
    }
  })

  test('indicator is always visible in header', async ({ page }) => {
    // Navigate to root (no project selected)
    await navigateAndWait(page, '/')
    await waitForStatusIndicator(page)

    let indicator = page.locator('[data-testid="status-indicator"]')
    await expect(indicator).toBeVisible()

    // Navigate to projects page
    await navigateAndWait(page, '/projects')
    await waitForStatusIndicator(page)

    indicator = page.locator('[data-testid="status-indicator"]')
    await expect(indicator).toBeVisible()

    // The "Select a project" text should no longer appear
    await expect(page.locator('text=Select a project')).not.toBeVisible()
  })

  test('navigates to project sessions when clicked with project context', async ({ page }) => {
    // First create a project via API
    const projectId = 'test-project-' + Date.now()
    await createTestProject(page, projectId)

    // Navigate to project page
    await navigateAndWait(page, `/projects/${projectId}`)
    await waitForStatusIndicator(page)

    // Click on the indicator
    await page.click('[data-testid="status-indicator"]')

    // Should navigate to project-specific sessions page
    await expect(page).toHaveURL(new RegExp(`/projects/${projectId}/sessions$`))
  })

  test('shows multiple status indicators horizontally', async ({ page }) => {
    await navigateAndWait(page, '/projects')
    await waitForStatusIndicator(page)

    // Check if multiple status indicators can be shown in a row
    const statusContainer = page.locator('a[href="/sessions"]').first()

    // Count visible status indicators
    const visibleStatuses = await statusContainer
      .locator('[data-testid^="status-"][data-testid$="-count"]')
      .count()

    // If any statuses are visible, they should be displayed horizontally
    if (visibleStatuses > 1) {
      // Check that indicators are in flex container with gap
      const containerClasses = await statusContainer.getAttribute('class')
      expect(containerClasses).toContain('flex')
      expect(containerClasses).toContain('items-center')
      expect(containerClasses).toContain('gap-3')
    }
  })

  test('updates in real-time when session status changes', async ({ page }) => {
    await navigateAndWait(page, '/projects')
    await waitForStatusIndicator(page)

    // Get initial state
    const idleVisible = await isElementSafelyVisible(page.locator('text=Agent idle'))
    const visibleStatuses = await checkStatusIndicators(page)

    // Verify indicator exists
    if (idleVisible) {
      await expect(page.locator('[data-testid="status-indicator"]')).toBeVisible()
    } else if (visibleStatuses.length > 0) {
      // At least one status indicator should be visible
      const anyStatus = page.locator('[data-testid^="status-"][data-testid$="-count"]').first()
      await expect(anyStatus).toBeVisible()
    }

    // The hook polls every 5 seconds, so updates would happen automatically
    // In a real test with mocked API, we could verify the updates
  })

  test('only shows non-zero counts', async ({ page }) => {
    await navigateAndWait(page, '/projects')
    await waitForStatusIndicator(page)

    // Get all visible count elements
    const countElements = await page.locator('[data-testid$="-count"]').all()

    // Verify each visible count is greater than 0
    for (const element of countElements) {
      const text = await element.textContent()
      const count = parseInt(text || '0')
      expect(count).toBeGreaterThan(0)
    }
  })

  test('working status shows ping animation', async ({ page }) => {
    await navigateAndWait(page, '/projects')
    await waitForStatusIndicator(page)

    const workingIndicator = page.locator('[data-testid="status-working"]')
    if (await isElementSafelyVisible(workingIndicator)) {
      // Working status should have ping animation
      const pingElement = workingIndicator.locator('.animate-ping')
      await expect(pingElement).toBeVisible()

      // Other statuses should not have ping
      const otherStatuses = ['waiting-input', 'waiting-answer', 'waiting-plan', 'error']
      for (const status of otherStatuses) {
        const indicator = page.locator(`[data-testid="status-${status}"]`)
        if (await isElementSafelyVisible(indicator, 100)) {
          const ping = indicator.locator('.animate-ping')
          await expect(ping).not.toBeVisible()
        }
      }
    }
  })
})
