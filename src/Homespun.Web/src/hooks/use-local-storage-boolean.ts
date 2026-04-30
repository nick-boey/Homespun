import { useCallback, useState } from 'react'

/**
 * `localStorage`-backed boolean state.
 *
 * Lazy-initialises from the persisted value (if any), writes through on
 * every set, and silently swallows `localStorage` failures (e.g. quota
 * exceeded, private-mode block). On read or write failure the returned
 * state still updates in-memory for the current session.
 */
export function useLocalStorageBoolean(
  key: string,
  defaultValue: boolean
): [boolean, (next: boolean) => void] {
  const [value, setValue] = useState<boolean>(() => {
    try {
      if (typeof window === 'undefined') return defaultValue
      const raw = window.localStorage.getItem(key)
      if (raw === null) return defaultValue
      return raw === 'true'
    } catch {
      return defaultValue
    }
  })

  const update = useCallback(
    (next: boolean) => {
      setValue(next)
      try {
        if (typeof window !== 'undefined') {
          window.localStorage.setItem(key, next ? 'true' : 'false')
        }
      } catch {
        // Swallow — in-memory state is already updated.
      }
    },
    [key]
  )

  return [value, update]
}
