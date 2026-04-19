/**
 * Test setup file for Vitest.
 */

// Mock localStorage/sessionStorage for jsdom v28+ (no longer built-in)
const createStorageMock = (): Storage => {
  let store: Record<string, string> = {}
  return {
    getItem: (key: string) => store[key] ?? null,
    setItem: (key: string, value: string) => {
      store[key] = value
    },
    removeItem: (key: string) => {
      delete store[key]
    },
    clear: () => {
      store = {}
    },
    get length() {
      return Object.keys(store).length
    },
    key: (index: number) => Object.keys(store)[index] ?? null,
  }
}
Object.defineProperty(window, 'localStorage', { value: createStorageMock() })
Object.defineProperty(window, 'sessionStorage', { value: createStorageMock() })

import '@testing-library/jest-dom/vitest'
import 'vitest-canvas-mock'

// Mock window.matchMedia for components that use media queries
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
})

// Mock ResizeObserver for components that use it (e.g., Radix UI)
class MockResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}
window.ResizeObserver = MockResizeObserver as unknown as typeof ResizeObserver

// Mock pointer capture methods for Radix UI
Object.defineProperty(HTMLElement.prototype, 'hasPointerCapture', {
  writable: true,
  value: () => false,
})

Object.defineProperty(HTMLElement.prototype, 'setPointerCapture', {
  writable: true,
  value: () => {},
})

Object.defineProperty(HTMLElement.prototype, 'releasePointerCapture', {
  writable: true,
  value: () => {},
})

// Mock scrollIntoView for Radix UI Select
Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
  writable: true,
  value: () => {},
})
