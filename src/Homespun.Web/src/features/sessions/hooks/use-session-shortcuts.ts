import { useEffect, useCallback, useRef } from 'react'
import { isInputElement } from '@/lib/utils/is-input-element'

export interface SessionShortcutCallbacks {
  onStopSession: () => void
  canStop: boolean
}

const DOUBLE_PRESS_TIMEOUT_MS = 500

export function useSessionShortcuts(callbacks: SessionShortcutCallbacks) {
  const { onStopSession, canStop } = callbacks

  // Track first CTRL+C press timestamp for double-press detection
  const firstPressTimeRef = useRef<number | null>(null)

  const handleKeyDown = useCallback(
    (event: KeyboardEvent) => {
      const { key, ctrlKey, metaKey, shiftKey } = event

      // Only handle CTRL+C or CMD+C (Mac)
      if (key.toLowerCase() !== 'c') return
      if (!(ctrlKey || metaKey)) return
      if (shiftKey) return // Don't intercept CTRL+SHIFT+C (copy formatted, etc.)

      // Can't stop if session isn't stoppable
      if (!canStop) return

      const isInInput = isInputElement(event.target)

      if (isInInput) {
        // In input: require double-press
        const now = Date.now()
        const firstPressTime = firstPressTimeRef.current

        if (firstPressTime && now - firstPressTime < DOUBLE_PRESS_TIMEOUT_MS) {
          // Double press detected
          event.preventDefault()
          firstPressTimeRef.current = null
          onStopSession()
        } else {
          // First press - record timestamp, let browser handle copy
          firstPressTimeRef.current = now
        }
      } else {
        // Not in input: single press triggers stop
        event.preventDefault()
        onStopSession()
      }
    },
    [onStopSession, canStop]
  )

  useEffect(() => {
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [handleKeyDown])
}
