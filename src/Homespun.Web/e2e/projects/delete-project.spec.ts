import { test, expect } from '@playwright/test'

/**
 * US4 — Delete a project.
 *
 * Deletion is confirmed via an AlertDialog on the home-page project card.
 * The server responds 204; the card should disappear from the list after
 * the `['projects']` query refetches.
 */
test.describe('Projects — delete', () => {
  test('confirming the dialog removes the card from the home list', async ({ page }) => {
    let deleteCalled = false
    await page.route('**/api/projects/demo-project', async (route) => {
      if (route.request().method() !== 'DELETE') {
        await route.fallback()
        return
      }
      deleteCalled = true
      await route.fulfill({ status: 204, body: '' })
    })

    // After delete, the list refetch should return an empty list.
    let getCallCount = 0
    await page.route('**/api/projects', async (route) => {
      if (route.request().method() !== 'GET') {
        await route.fallback()
        return
      }
      getCallCount++
      if (getCallCount === 1) {
        await route.fallback()
        return
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      })
    })

    await page.goto('/')
    await page.waitForLoadState('networkidle')

    const card = page.getByTestId('project-card-link').filter({ hasText: 'Demo Project' })
    await expect(card.first()).toBeVisible()

    await page
      .getByRole('button', { name: /delete project/i })
      .first()
      .click()

    const dialog = page.getByRole('alertdialog')
    await expect(dialog).toBeVisible()
    await dialog.getByRole('button', { name: 'Delete' }).click()

    await expect(card).toHaveCount(0)
    expect(deleteCalled).toBe(true)
  })

  test('cancelling the dialog leaves the card in place', async ({ page }) => {
    let deleteCalled = false
    await page.route('**/api/projects/demo-project', async (route) => {
      if (route.request().method() !== 'DELETE') {
        await route.fallback()
        return
      }
      deleteCalled = true
      await route.fulfill({ status: 204, body: '' })
    })

    await page.goto('/')
    await page.waitForLoadState('networkidle')

    await page
      .getByRole('button', { name: /delete project/i })
      .first()
      .click()

    const dialog = page.getByRole('alertdialog')
    await expect(dialog).toBeVisible()
    await dialog.getByRole('button', { name: 'Cancel' }).click()

    const card = page.getByTestId('project-card-link').filter({ hasText: 'Demo Project' })
    await expect(card.first()).toBeVisible()
    expect(deleteCalled).toBe(false)
  })
})
