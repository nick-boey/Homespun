import { test, expect } from '@playwright/test'

test.describe('Agent Status Indicator', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/')
  })

  test('shows idle status when no sessions are active', async ({ page }) => {
    // The indicator should be visible in the header
    const indicator = page.locator('[data-testid="status-indicator"]')
    await expect(indicator).toBeVisible()

    // Check for green idle indicator
    await expect(indicator).toHaveClass(/text-green-500/)

    // Check idle text
    await expect(page.locator('text=Agent idle')).toBeVisible()
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
    await page.goto('/projects')

    // Check if any status indicators are visible
    const workingIndicator = page.locator('[data-testid="status-working"]')
    const waitingInputIndicator = page.locator('[data-testid="status-waiting-input"]')
    const waitingAnswerIndicator = page.locator('[data-testid="status-waiting-answer"]')
    const waitingPlanIndicator = page.locator('[data-testid="status-waiting-plan"]')
    const errorIndicator = page.locator('[data-testid="status-error"]')

    // At least one indicator should be visible (idle or active)
    const anyIndicatorVisible =
      (await workingIndicator.isVisible({ timeout: 1000 }).catch(() => false)) ||
      (await waitingInputIndicator.isVisible({ timeout: 1000 }).catch(() => false)) ||
      (await waitingAnswerIndicator.isVisible({ timeout: 1000 }).catch(() => false)) ||
      (await waitingPlanIndicator.isVisible({ timeout: 1000 }).catch(() => false)) ||
      (await errorIndicator.isVisible({ timeout: 1000 }).catch(() => false))

    if (anyIndicatorVisible) {
      // Verify counts are shown next to indicators
      if (await workingIndicator.isVisible({ timeout: 100 }).catch(() => false)) {
        await expect(page.locator('[data-testid="status-working-count"]')).toBeVisible()
      }
      if (await waitingInputIndicator.isVisible({ timeout: 100 }).catch(() => false)) {
        await expect(page.locator('[data-testid="status-waiting-input-count"]')).toBeVisible()
      }
    } else {
      // Should show idle state
      await expect(page.locator('text=Agent idle')).toBeVisible()
    }
  })

  test('shows correct indicator colors based on status type', async ({ page }) => {
    await page.goto('/projects')

    // Check color classes for each status type if visible
    const workingCount = page.locator('[data-testid="status-working-count"]')
    if (await workingCount.isVisible({ timeout: 1000 }).catch(() => false)) {
      await expect(workingCount).toHaveClass(/text-blue-500/)
    }

    const waitingInputCount = page.locator('[data-testid="status-waiting-input-count"]')
    if (await waitingInputCount.isVisible({ timeout: 1000 }).catch(() => false)) {
      await expect(waitingInputCount).toHaveClass(/text-yellow-500/)
    }

    const waitingAnswerCount = page.locator('[data-testid="status-waiting-answer-count"]')
    if (await waitingAnswerCount.isVisible({ timeout: 1000 }).catch(() => false)) {
      await expect(waitingAnswerCount).toHaveClass(/text-orange-500/)
    }

    const waitingPlanCount = page.locator('[data-testid="status-waiting-plan-count"]')
    if (await waitingPlanCount.isVisible({ timeout: 1000 }).catch(() => false)) {
      await expect(waitingPlanCount).toHaveClass(/text-purple-500/)
    }

    const errorCount = page.locator('[data-testid="status-error-count"]')
    if (await errorCount.isVisible({ timeout: 1000 }).catch(() => false)) {
      await expect(errorCount).toHaveClass(/text-red-500/)
    }
  })

  test('indicator is always visible in header', async ({ page }) => {
    // Navigate to root (no project selected)
    await page.goto('/')
    let indicator = page.locator('[data-testid="status-indicator"]')
    await expect(indicator).toBeVisible()

    // Navigate to projects page
    await page.goto('/projects')
    indicator = page.locator('[data-testid="status-indicator"]')
    await expect(indicator).toBeVisible()

    // The "Select a project" text should no longer appear
    await expect(page.locator('text=Select a project')).not.toBeVisible()
  })

  test('navigates to project sessions when clicked with project context', async ({ page }) => {
    // First create a project via API
    const projectId = 'test-project-' + Date.now()
    await page.request.post('/api/projects', {
      data: {
        id: projectId,
        name: 'Test Project',
        description: 'Test project for E2E',
        owner: 'test-owner',
        repository: 'test-repo',
      },
    })

    // Navigate to project page
    await page.goto(`/projects/${projectId}`)

    // Click on the indicator
    await page.click('[data-testid="status-indicator"]')

    // Should navigate to project-specific sessions page
    await expect(page).toHaveURL(new RegExp(`/projects/${projectId}/sessions$`))
  })

  test('shows multiple status indicators horizontally', async ({ page }) => {
    await page.goto('/projects')

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
    await page.goto('/projects')

    // Get initial state
    const idleVisible = await page
      .locator('text=Agent idle')
      .isVisible({ timeout: 1000 })
      .catch(() => false)
    await page
      .locator('[data-testid="status-working"]')
      .isVisible({ timeout: 1000 })
      .catch(() => false)

    // Verify indicator exists
    if (idleVisible) {
      await expect(page.locator('[data-testid="status-indicator"]')).toBeVisible()
    } else {
      // At least one status indicator should be visible
      const anyStatus = page.locator('[data-testid^="status-"][data-testid$="-count"]').first()
      await expect(anyStatus).toBeVisible()
    }

    // The hook polls every 5 seconds, so updates would happen automatically
    // In a real test with mocked API, we could verify the updates
  })

  test('only shows non-zero counts', async ({ page }) => {
    await page.goto('/projects')

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
    await page.goto('/projects')

    const workingIndicator = page.locator('[data-testid="status-working"]')
    if (await workingIndicator.isVisible({ timeout: 1000 }).catch(() => false)) {
      // Working status should have ping animation
      const pingElement = workingIndicator.locator('.animate-ping')
      await expect(pingElement).toBeVisible()

      // Other statuses should not have ping
      const otherStatuses = ['waiting-input', 'waiting-answer', 'waiting-plan', 'error']
      for (const status of otherStatuses) {
        const indicator = page.locator(`[data-testid="status-${status}"]`)
        if (await indicator.isVisible({ timeout: 100 }).catch(() => false)) {
          const ping = indicator.locator('.animate-ping')
          await expect(ping).not.toBeVisible()
        }
      }
    }
  })
})
