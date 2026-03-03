import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for the type change menu functionality in TaskGraphView.
 * Verifies that clicking the type badge opens a dropdown menu to change the issue type.
 *
 * Mirrors: tests/Homespun.E2E.Tests/TypeChangeMenuTests.cs
 */
test.describe('Type Change Menu', () => {
  test('type badge click opens menu', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Verify type badge is visible
    const typeBadge = page.locator('.task-graph-issue-type').first()
    await expect(typeBadge).toBeVisible({ timeout: 5000 })

    // Verify menu is not visible initially
    const typeMenu = page.locator('.task-graph-type-menu')
    await expect(typeMenu).not.toBeVisible()

    // Click the type badge
    await typeBadge.click()

    // Verify the menu appears with 4 type options
    await expect(typeMenu).toBeVisible({ timeout: 5000 })

    const menuButtons = typeMenu.locator('button')
    await expect(menuButtons).toHaveCount(4)

    // Verify button labels
    await expect(menuButtons.nth(0)).toHaveText('Bug')
    await expect(menuButtons.nth(1)).toHaveText('Task')
    await expect(menuButtons.nth(2)).toHaveText('Feature')
    await expect(menuButtons.nth(3)).toHaveText('Chore')
  })

  test('type badge clicking again closes menu', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Get the type badge
    const typeBadge = page.locator('.task-graph-issue-type').first()
    const typeMenu = page.locator('.task-graph-type-menu')

    // Open the menu
    await typeBadge.click()
    await expect(typeMenu).toBeVisible({ timeout: 5000 })

    // Click badge again to close
    await typeBadge.click()
    await expect(typeMenu).not.toBeVisible({ timeout: 5000 })
  })

  test('type menu selecting type closes menu', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Get the type badge
    const typeBadge = page.locator('.task-graph-issue-type').first()
    const typeMenu = page.locator('.task-graph-type-menu')

    // Open the menu
    await typeBadge.click()
    await expect(typeMenu).toBeVisible({ timeout: 5000 })

    // Click a type button (Bug)
    const bugButton = typeMenu.locator('button.bug')
    await bugButton.click()

    // Menu should close after selection
    await expect(typeMenu).not.toBeVisible({ timeout: 5000 })
  })

  test('type badge click does not trigger row click', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Get the type badge on the first row
    const typeBadge = page.locator('.task-graph-issue-type').first()
    const typeMenu = page.locator('.task-graph-type-menu')

    // Note the current URL before clicking
    const urlBeforeClick = page.url()

    // Click the type badge - should open menu but NOT navigate or select the row
    await typeBadge.click()

    // Verify menu opened
    await expect(typeMenu).toBeVisible({ timeout: 5000 })

    // Verify the URL hasn't changed (no navigation happened)
    expect(page.url()).toBe(urlBeforeClick)
  })
})
