import { test, expect } from '@playwright/test'
import { createMockSession } from '../utils/test-helpers'

/**
 * FI-1 / US6: exercise the stop affordance on an active session and assert the
 * UI reflects the resulting state. The interrupt and clear-context flows live
 * on the bottom sheet / session controls; this spec covers the always-visible
 * Stop button in the session header (the most user-visible terminal control).
 *
 * Interrupt and Clear-context are tracked as TODO follow-ups — see the spec
 * delta in `openspec/changes/close-out-claude-agent-sessions-migration-gaps/
 * specs/claude-agent-sessions/spec.md` US6 scenarios.
 */
test.describe('US6 — stop a session', () => {
  test('clicking Stop transitions the session out of running state', async ({
    page,
    request,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    const sessionId = await createMockSession(request)

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    await expect(page.getByPlaceholder('Type a message...')).toBeEnabled({ timeout: 15000 })

    const stopButton = page.getByTestId('session-stop')
    await expect(stopButton).toBeVisible({ timeout: 10000 })
    await stopButton.click()

    // The header Stop button opens an AlertDialog confirmation; the actual
    // mutation only fires when the user confirms via the dialog's Stop button.
    const stopDialog = page.getByRole('alertdialog', { name: 'Stop Session' })
    await expect(stopDialog).toBeVisible({ timeout: 5000 })
    await stopDialog.getByRole('button', { name: 'Stop' }).click()

    // After confirming, the session detail view is either redirected to
    // /sessions (default UX) or the Stop button disappears because
    // `showStopButton` flips off. Assert one of those terminal states.
    await expect
      .poll(
        async () => {
          if (page.url().endsWith('/sessions')) {
            return 'redirected'
          }
          const stillVisible = await stopButton.isVisible().catch(() => false)
          return stillVisible ? 'visible' : 'hidden'
        },
        { timeout: 15000 }
      )
      .not.toEqual('visible')
  })
})
