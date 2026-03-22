import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useSessionShortcuts, type SessionShortcutCallbacks } from './use-session-shortcuts'

describe('useSessionShortcuts', () => {
  let callbacks: SessionShortcutCallbacks

  beforeEach(() => {
    vi.useFakeTimers()
    callbacks = {
      onStopSession: vi.fn(),
      canStop: true,
    }
  })

  afterEach(() => {
    vi.clearAllMocks()
    vi.useRealTimers()
  })

  function dispatchKeyDown(key: string, options: Partial<KeyboardEvent> = {}) {
    const event = new KeyboardEvent('keydown', {
      key,
      bubbles: true,
      ...options,
    })
    document.dispatchEvent(event)
  }

  function dispatchKeyDownOnElement(
    element: HTMLElement,
    key: string,
    options: Partial<KeyboardEvent> = {}
  ) {
    const event = new KeyboardEvent('keydown', {
      key,
      bubbles: true,
      ...options,
    })
    Object.defineProperty(event, 'target', { value: element })
    document.dispatchEvent(event)
  }

  it('calls onStopSession when CTRL+C is pressed (not in input)', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    dispatchKeyDown('c', { ctrlKey: true })
    expect(callbacks.onStopSession).toHaveBeenCalled()
  })

  it('calls onStopSession when CMD+C is pressed on Mac (not in input)', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    dispatchKeyDown('c', { metaKey: true })
    expect(callbacks.onStopSession).toHaveBeenCalled()
  })

  it('does not call onStopSession when canStop is false', () => {
    const disabledCallbacks = { ...callbacks, canStop: false }
    renderHook(() => useSessionShortcuts(disabledCallbacks))

    dispatchKeyDown('c', { ctrlKey: true })
    expect(disabledCallbacks.onStopSession).not.toHaveBeenCalled()
  })

  it('does not trigger on single CTRL+C when focused on input element', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()

    dispatchKeyDownOnElement(input, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()

    document.body.removeChild(input)
  })

  it('does not trigger on single CTRL+C when focused on textarea', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    const textarea = document.createElement('textarea')
    document.body.appendChild(textarea)
    textarea.focus()

    dispatchKeyDownOnElement(textarea, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()

    document.body.removeChild(textarea)
  })

  it('triggers on double CTRL+C within 500ms when in input', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()

    // First CTRL+C
    dispatchKeyDownOnElement(input, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()

    // Second CTRL+C within 500ms
    act(() => {
      vi.advanceTimersByTime(300)
    })
    dispatchKeyDownOnElement(input, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).toHaveBeenCalled()

    document.body.removeChild(input)
  })

  it('does not trigger if second CTRL+C comes after 500ms timeout', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()

    // First CTRL+C
    dispatchKeyDownOnElement(input, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()

    // Wait more than 500ms
    act(() => {
      vi.advanceTimersByTime(600)
    })

    // Second CTRL+C after timeout
    dispatchKeyDownOnElement(input, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()

    document.body.removeChild(input)
  })

  it('cleans up event listener on unmount', () => {
    const removeEventListenerSpy = vi.spyOn(document, 'removeEventListener')
    const { unmount } = renderHook(() => useSessionShortcuts(callbacks))

    unmount()

    expect(removeEventListenerSpy).toHaveBeenCalledWith('keydown', expect.any(Function))
    removeEventListenerSpy.mockRestore()
  })

  it('does not trigger on CTRL+SHIFT+C', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    dispatchKeyDown('c', { ctrlKey: true, shiftKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()
  })

  it('does not trigger on just C key without modifier', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    dispatchKeyDown('c')
    expect(callbacks.onStopSession).not.toHaveBeenCalled()
  })

  it('does not trigger on CTRL+V', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    dispatchKeyDown('v', { ctrlKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()
  })

  it('does not trigger on CTRL+X', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    dispatchKeyDown('x', { ctrlKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()
  })

  it('resets double-press state after triggering', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()

    // Double press triggers
    dispatchKeyDownOnElement(input, 'c', { ctrlKey: true })
    act(() => {
      vi.advanceTimersByTime(300)
    })
    dispatchKeyDownOnElement(input, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).toHaveBeenCalledTimes(1)

    // Next single press should not trigger (state reset)
    dispatchKeyDownOnElement(input, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).toHaveBeenCalledTimes(1)

    document.body.removeChild(input)
  })

  it('does not trigger on contenteditable with single press', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    const div = document.createElement('div')
    div.contentEditable = 'true'
    document.body.appendChild(div)
    div.focus()

    dispatchKeyDownOnElement(div, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).not.toHaveBeenCalled()

    document.body.removeChild(div)
  })

  it('triggers on contenteditable with double press', () => {
    renderHook(() => useSessionShortcuts(callbacks))

    const div = document.createElement('div')
    div.contentEditable = 'true'
    document.body.appendChild(div)
    div.focus()

    // Double press
    dispatchKeyDownOnElement(div, 'c', { ctrlKey: true })
    act(() => {
      vi.advanceTimersByTime(300)
    })
    dispatchKeyDownOnElement(div, 'c', { ctrlKey: true })
    expect(callbacks.onStopSession).toHaveBeenCalled()

    document.body.removeChild(div)
  })
})
