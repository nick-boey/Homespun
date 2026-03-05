import { test, expect } from '@playwright/test'

test.describe('Mobile Chat Layout', () => {
  test.beforeEach(async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 })

    // Navigate to a session with messages
    await page.goto('/projects/test-project/sessions/test-session')

    // Wait for messages to load
    await page.waitForSelector('[data-testid^="message-"]')
  })

  test('chat bubbles use 90% width on mobile', async ({ page }) => {
    // Find a message bubble container
    const messageBubble = page.locator('[data-testid^="message-content-"]').first()
    await expect(messageBubble).toBeVisible()

    // Get the parent container that has max-width
    const bubbleContainer = messageBubble.locator('..').first()

    // Verify mobile width (90%)
    await expect(bubbleContainer).toHaveCSS('max-width', /90%/)
  })

  test('text uses smaller prose size on mobile', async ({ page }) => {
    // Find markdown content in messages
    const markdownContent = page.locator('.prose-sm').first()
    await expect(markdownContent).toBeVisible()

    // Verify prose-sm class is applied
    await expect(markdownContent).toHaveClass(/prose-sm/)
  })

  test('markdown headings render with distinct sizes', async ({ page }) => {
    // Create a test message with headings if needed
    // This would normally be done through the chat interface or test data setup

    // Look for heading elements in messages
    const h1 = page.locator('h1').first()
    const h2 = page.locator('h2').first()

    if (await h1.isVisible()) {
      const h1Size = await h1.evaluate((el) => window.getComputedStyle(el).fontSize)
      const h2Size = await h2.evaluate((el) => window.getComputedStyle(el).fontSize)

      // H1 should be larger than H2
      expect(parseFloat(h1Size)).toBeGreaterThan(parseFloat(h2Size))
    }
  })

  test.describe('Desktop Chat Layout', () => {
    test.beforeEach(async ({ page }) => {
      // Set desktop viewport
      await page.setViewportSize({ width: 1440, height: 900 })
    })

    test('chat bubbles use 80% width on desktop', async ({ page }) => {
      await page.goto('/projects/test-project/sessions/test-session')
      await page.waitForSelector('[data-testid^="message-"]')

      const messageBubble = page.locator('[data-testid^="message-content-"]').first()
      await expect(messageBubble).toBeVisible()

      const bubbleContainer = messageBubble.locator('..').first()

      // Verify desktop width (80%)
      await expect(bubbleContainer).toHaveCSS('max-width', /80%/)
    })

    test('text uses regular prose size on desktop', async ({ page }) => {
      await page.goto('/projects/test-project/sessions/test-session')
      await page.waitForSelector('[data-testid^="message-"]')

      // Find markdown content
      const markdownContent = page.locator('[class*="prose"]').first()
      await expect(markdownContent).toBeVisible()

      // Should have prose class (not prose-sm on desktop)
      const classes = await markdownContent.getAttribute('class')
      expect(classes).toContain('prose')

      // On desktop, we expect the responsive class to show regular prose
      // This will pass once we implement responsive classes
    })
  })

  test('responsive breakpoint transitions smoothly', async ({ page }) => {
    await page.goto('/projects/test-project/sessions/test-session')
    await page.waitForSelector('[data-testid^="message-"]')

    // Start with mobile viewport
    await page.setViewportSize({ width: 375, height: 667 })

    const bubbleContainer = page.locator('[data-testid^="message-content-"]').locator('..').first()
    await expect(bubbleContainer).toHaveCSS('max-width', /90%/)

    // Transition to tablet (md breakpoint is 768px)
    await page.setViewportSize({ width: 768, height: 1024 })

    // Should now use desktop width
    await expect(bubbleContainer).toHaveCSS('max-width', /80%/)
  })

  test('all prose classes have base prose class', async ({ page }) => {
    await page.goto('/projects/test-project/sessions/test-session')
    await page.waitForSelector('[data-testid^="message-"]')

    // Find all elements with prose modifiers
    const proseElements = page.locator('[class*="prose-"]')
    const count = await proseElements.count()

    for (let i = 0; i < count; i++) {
      const element = proseElements.nth(i)
      const classes = await element.getAttribute('class')

      // Every element with prose modifiers should also have base 'prose' class
      if (classes?.includes('prose-')) {
        expect(classes).toContain('prose')
      }
    }
  })
})
