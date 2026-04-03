import { test, expect } from '@playwright/test'
import { createMockSession } from './utils/test-helpers'

test.describe('Mention search', () => {
  test.beforeEach(async ({ page }) => {
    // Mock the search API endpoints
    await page.route('**/api/projects/*/search/files*', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          files: [
            'src/index.ts',
            'src/components/Button.tsx',
            'src/utils/helpers.ts',
            'package.json',
            'README.md',
          ],
          hash: 'abc123',
        }),
      })
    )

    await page.route('**/api/projects/*/search/prs*', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          prs: [
            { number: 123, title: 'Add feature X', branchName: 'feature/x' },
            { number: 456, title: 'Fix bug Y', branchName: 'fix/y' },
            { number: 789, title: 'Update documentation', branchName: 'docs/update' },
          ],
          hash: 'def456',
        }),
      })
    )
  })

  test.describe('Issue description field', () => {
    test.beforeEach(async ({ page }) => {
      // Navigate to issue edit page (using demo-project and ISSUE-001 from mock data)
      await page.goto('/projects/demo-project/issues/ISSUE-001/edit')
    })

    test('shows file search popup when typing @', async ({ page }) => {
      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('@')

      // Popup should appear
      const popup = page.getByRole('listbox', { name: /file search results/i })
      await expect(popup).toBeVisible()

      // Should show file options
      await expect(page.getByText('src/index.ts')).toBeVisible()
    })

    test('filters files as user types', async ({ page }) => {
      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('@button')

      // Should filter to matching file
      await expect(page.getByText('Button.tsx')).toBeVisible()
      await expect(page.getByText('README.md')).not.toBeVisible()
    })

    test('inserts file reference on selection', async ({ page }) => {
      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('@')

      // Click on a file option
      await page.getByText('src/index.ts').click()

      // Should insert formatted reference
      await expect(textarea).toHaveValue('@src/index.ts')
    })

    test('shows PR search popup when typing #', async ({ page }) => {
      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('#')

      // Popup should appear
      const popup = page.getByRole('listbox', { name: /pr search results/i })
      await expect(popup).toBeVisible()

      // Should show PR options
      await expect(page.getByText('#123')).toBeVisible()
      await expect(page.getByText('Add feature X')).toBeVisible()
    })

    test('filters PRs by number', async ({ page }) => {
      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('#123')

      // Should filter to matching PR - use popup listbox to avoid matching textarea content
      const popup = page.getByRole('listbox', { name: /pr search results/i })
      await expect(popup.getByText('#123')).toBeVisible()
      await expect(popup.getByText('#456')).not.toBeVisible()
    })

    test('inserts PR reference on selection', async ({ page }) => {
      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('#')

      // Click on a PR option
      await page.getByText('#123').click()

      // Should insert formatted reference
      await expect(textarea).toHaveValue('PR #123')
    })

    test('closes popup on Escape key', async ({ page }) => {
      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('@')

      // Popup should appear
      const popup = page.getByRole('listbox', { name: /file search results/i })
      await expect(popup).toBeVisible()

      // Press Escape
      await page.keyboard.press('Escape')

      // Popup should close
      await expect(popup).not.toBeVisible()
    })

    test('navigates with arrow keys', async ({ page }) => {
      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('@')

      // Wait for popup
      await expect(page.getByRole('listbox')).toBeVisible()

      // Press down arrow to select second item
      await page.keyboard.press('ArrowDown')

      // Second item should be highlighted (aria-selected)
      const secondItem = page.getByRole('option').nth(1)
      await expect(secondItem).toHaveAttribute('aria-selected', 'true')
    })
  })

  test.describe('Chat input', () => {
    let sessionId: string

    test.beforeAll(async ({ request }) => {
      // Create a session via API since there are no pre-seeded sessions
      sessionId = await createMockSession(request)
    })

    test.beforeEach(async ({ page }) => {
      // Navigate to the dynamically created session
      await page.goto(`/sessions/${sessionId}`)
      // Wait for session to load
      await page.waitForLoadState('networkidle')
    })

    test('shows file search popup when typing @ in chat', async ({ page }) => {
      // Find the chat input textarea
      const textarea = page.getByPlaceholder(/type a message/i)
      await textarea.click()
      await textarea.fill('@')

      // Popup should appear
      const popup = page.getByRole('listbox', { name: /file search results/i })
      await expect(popup).toBeVisible()
    })

    test('shows PR search popup when typing # in chat', async ({ page }) => {
      const textarea = page.getByPlaceholder(/type a message/i)
      await textarea.click()
      await textarea.fill('#')

      // Popup should appear
      const popup = page.getByRole('listbox', { name: /pr search results/i })
      await expect(popup).toBeVisible()
    })
  })

  test.describe('No data scenarios', () => {
    test('shows empty state when no files found', async ({ page }) => {
      // Override the files mock to return empty
      await page.route('**/api/projects/*/search/files*', (route) =>
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ files: [], hash: 'empty' }),
        })
      )

      await page.goto('/projects/demo-project/issues/ISSUE-001/edit')

      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('@nonexistent')

      // Should show no files message
      await expect(page.getByText(/no files found/i)).toBeVisible()
    })

    test('shows empty state when no PRs found', async ({ page }) => {
      // Override the PRs mock to return empty
      await page.route('**/api/projects/*/search/prs*', (route) =>
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ prs: [], hash: 'empty' }),
        })
      )

      await page.goto('/projects/demo-project/issues/ISSUE-001/edit')

      const textarea = page.getByPlaceholder(/describe the issue/i)
      await textarea.click()
      await textarea.fill('#999')

      // Should show no PRs message
      await expect(page.getByText(/no pull requests found/i)).toBeVisible()
    })
  })
})
