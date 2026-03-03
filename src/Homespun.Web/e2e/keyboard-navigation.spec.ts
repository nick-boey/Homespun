import { test, expect } from '@playwright/test'

/**
 * End-to-end tests for Vim-like keyboard navigation in the task graph.
 * Verifies that inline editing works correctly with real browser input.
 *
 * Mirrors: tests/Homespun.E2E.Tests/KeyboardNavigationTests.cs
 */
test.describe('Keyboard Navigation', () => {
  test('insert mode - typing appends text to input', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press j to select the first issue
    await page.keyboard.press('j')

    // Verify a row is selected
    const selectedRow = page.locator('.task-graph-row-selected')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Press i to enter insert mode (cursor at start)
    await page.keyboard.press('i')

    // Verify the inline editor input appears
    const input = page.locator('input.inline-issue-input')
    await expect(input).toBeVisible({ timeout: 5000 })

    // Verify the INSERT mode indicator is shown
    const insertIndicator = page.locator('text=-- INSERT --')
    await expect(insertIndicator).toBeVisible({ timeout: 5000 })

    // Type additional text
    await page.keyboard.type(' appended')

    // Verify the input contains appended text
    await expect(input).toHaveValue(/appended/)

    // Press Escape to cancel editing
    await page.keyboard.press('Escape')

    // Verify edit mode is exited (INSERT indicator gone)
    await expect(insertIndicator).not.toBeVisible({ timeout: 5000 })

    // Verify we're back to viewing mode with selection preserved
    await expect(selectedRow).toBeVisible({ timeout: 5000 })
  })

  test('replace mode - typing replaces input text', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press j to select, then r to enter replace mode
    await page.keyboard.press('j')
    const selectedRow = page.locator('.task-graph-row-selected')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    await page.keyboard.press('r')

    // Verify the inline editor appears
    const input = page.locator('input.inline-issue-input')
    await expect(input).toBeVisible({ timeout: 5000 })

    // Type replacement text
    await page.keyboard.type('replaced title')

    // Verify the input shows the replacement text
    await expect(input).toHaveValue('replaced title')

    // Cancel to avoid persisting changes
    await page.keyboard.press('Escape')
  })

  test('create issue below - typing adds text to new input', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press j to select, then o to create new issue below
    await page.keyboard.press('j')
    const selectedRow = page.locator('.task-graph-row-selected')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    await page.keyboard.press('o')

    // Verify the inline editor input appears
    const input = page.locator('input.inline-issue-input')
    await expect(input).toBeVisible({ timeout: 5000 })

    // Type new issue title
    await page.keyboard.type('new issue title')

    // Verify the input shows the typed text
    await expect(input).toHaveValue('new issue title')

    // Cancel to avoid persisting changes
    await page.keyboard.press('Escape')
  })

  test('navigation keys work with selective keyboard prevention', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press j to select the first issue
    await page.keyboard.press('j')

    // Verify a row is selected
    const selectedRow = page.locator('.task-graph-row-selected')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Navigation should still work after setup - press j again to move down
    await page.keyboard.press('j')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Press k to move back up
    await page.keyboard.press('k')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })
  })

  test('enter key when issue selected navigates to edit page', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press j to select the first issue
    await page.keyboard.press('j')

    // Verify a row is selected
    const selectedRow = page.locator('.task-graph-row-selected')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Press Enter to open the edit page
    await page.keyboard.press('Enter')

    // Verify we navigated to the edit page by checking the URL contains /edit
    await expect(page).toHaveURL(/\/issues\/.+\/edit/, { timeout: 5000 })
  })

  test('search highlights matching issues while typing', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press / to focus the toolbar search input
    await page.keyboard.press('/')

    // Verify the toolbar search input is focused
    const searchInput = page.locator('[data-testid="toolbar-search-input"]')
    await expect(searchInput).toBeFocused({ timeout: 5000 })

    // Type a search term that should match some issues
    await page.keyboard.type('issue')

    // Verify that matching issues are highlighted while typing (before pressing Enter)
    const highlightedRows = page.locator('.task-graph-row-search-match')
    await expect(highlightedRows.first()).toBeVisible({ timeout: 5000 })

    // Press Escape to cancel search
    await page.keyboard.press('Escape')
  })

  test('search enter selects first match and restores focus', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press / to focus the toolbar search input
    await page.keyboard.press('/')

    // Verify the toolbar search input is focused
    const searchInput = page.locator('[data-testid="toolbar-search-input"]')
    await expect(searchInput).toBeFocused({ timeout: 5000 })

    // Type a search term
    await page.keyboard.type('issue')

    // Press Enter to embed search and select first match
    await page.keyboard.press('Enter')

    // Verify the search input is no longer focused after embedding
    await expect(searchInput).not.toBeFocused({ timeout: 5000 })

    // Verify a row is selected (first match)
    const selectedRow = page.locator('.task-graph-row-selected')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Verify highlighting is still visible after embedding
    const highlightedRows = page.locator('.task-graph-row-search-match')
    await expect(highlightedRows.first()).toBeVisible({ timeout: 5000 })

    // Verify keyboard navigation works after embedding (focus was restored)
    // Press j to move down - should work because focus is restored to page container
    await page.keyboard.press('j')

    // Selection should have changed (focus is working)
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Press Escape to clear search
    await page.keyboard.press('Escape')

    // Verify highlights are cleared
    await expect(highlightedRows).not.toBeVisible({ timeout: 5000 })
  })

  test('search next and previous match navigation', async ({ page }) => {
    // Navigate to the demo project page
    await page.goto('/projects/demo-project')
    await page.waitForLoadState('networkidle')

    // Wait for task graph to render
    const taskGraphRow = page.locator('.task-graph-row').first()
    await expect(taskGraphRow).toBeVisible({ timeout: 10000 })

    // Press / to start search, type term, and embed
    await page.keyboard.press('/')
    await page.keyboard.type('issue')
    await page.keyboard.press('Enter')

    // Verify search bar is hidden
    const searchBar = page.locator('[data-testid="search-bar"]')
    await expect(searchBar).not.toBeVisible({ timeout: 5000 })

    // Verify a match is selected
    const selectedRow = page.locator('.task-graph-row-selected')
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Get the ID of the first selected issue
    const firstSelectedId = await selectedRow.getAttribute('data-issue-id')

    // Press n to go to next match
    await page.keyboard.press('n')

    // Verify the selected row still exists (navigation worked)
    await expect(selectedRow).toBeVisible({ timeout: 5000 })

    // Press Shift+N (capital N) to go to previous match
    await page.keyboard.press('N')

    // Verify we're back to the first match
    const backToFirst = await selectedRow.getAttribute('data-issue-id')
    expect(backToFirst).toBe(firstSelectedId)

    // Press Escape to clear search
    await page.keyboard.press('Escape')
  })
})
