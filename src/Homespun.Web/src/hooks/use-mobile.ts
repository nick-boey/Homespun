import { useSyncExternalStore } from 'react'

/**
 * Mobile breakpoint (matches Tailwind's md breakpoint)
 */
const MOBILE_BREAKPOINT = 768

/**
 * Subscribe to media query changes
 */
function subscribeToMediaQuery(callback: () => void): () => void {
  const mediaQuery = window.matchMedia(`(max-width: ${MOBILE_BREAKPOINT - 1}px)`)
  mediaQuery.addEventListener('change', callback)
  return () => mediaQuery.removeEventListener('change', callback)
}

/**
 * Get current mobile state from media query
 */
function getSnapshot(): boolean {
  return window.matchMedia(`(max-width: ${MOBILE_BREAKPOINT - 1}px)`).matches
}

/**
 * Server snapshot (assume desktop)
 */
function getServerSnapshot(): boolean {
  return false
}

/**
 * Hook to detect if the current viewport is mobile-sized.
 * Uses the md breakpoint (768px) as the threshold.
 *
 * @returns true if viewport width is less than 768px, false otherwise
 */
export function useMobile(): boolean {
  return useSyncExternalStore(subscribeToMediaQuery, getSnapshot, getServerSnapshot)
}
