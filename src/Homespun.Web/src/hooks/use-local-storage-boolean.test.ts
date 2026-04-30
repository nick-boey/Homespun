import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useLocalStorageBoolean } from './use-local-storage-boolean'

describe('useLocalStorageBoolean', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('returns defaultValue when key is absent', () => {
    const { result } = renderHook(() => useLocalStorageBoolean('absent-key', true))
    expect(result.current[0]).toBe(true)

    const { result: result2 } = renderHook(() => useLocalStorageBoolean('absent-key-2', false))
    expect(result2.current[0]).toBe(false)
  })

  it('returns persisted value when key is present', () => {
    window.localStorage.setItem('present-true', 'true')
    window.localStorage.setItem('present-false', 'false')

    const { result: trueResult } = renderHook(() => useLocalStorageBoolean('present-true', false))
    expect(trueResult.current[0]).toBe(true)

    const { result: falseResult } = renderHook(() => useLocalStorageBoolean('present-false', true))
    expect(falseResult.current[0]).toBe(false)
  })

  it('writes the new value to localStorage on update', () => {
    const { result } = renderHook(() => useLocalStorageBoolean('update-key', true))

    act(() => {
      result.current[1](false)
    })

    expect(result.current[0]).toBe(false)
    expect(window.localStorage.getItem('update-key')).toBe('false')

    act(() => {
      result.current[1](true)
    })

    expect(result.current[0]).toBe(true)
    expect(window.localStorage.getItem('update-key')).toBe('true')
  })

  it('still updates in-memory state when localStorage.setItem throws', () => {
    const setItemSpy = vi.spyOn(window.localStorage, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceeded')
    })

    const { result } = renderHook(() => useLocalStorageBoolean('quota-key', true))

    expect(() => {
      act(() => {
        result.current[1](false)
      })
    }).not.toThrow()

    expect(result.current[0]).toBe(false)
    expect(setItemSpy).toHaveBeenCalled()
  })

  it('falls back to defaultValue when localStorage.getItem throws on read', () => {
    vi.spyOn(window.localStorage, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError')
    })

    const { result } = renderHook(() => useLocalStorageBoolean('throwing-read', true))

    expect(result.current[0]).toBe(true)
  })

  it('treats invalid persisted strings (non "true") as false', () => {
    window.localStorage.setItem('garbage-key', 'not-a-bool')

    const { result } = renderHook(() => useLocalStorageBoolean('garbage-key', true))

    expect(result.current[0]).toBe(false)
  })
})
