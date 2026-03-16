import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useToolbarShortcuts, type ToolbarShortcutCallbacks } from './use-toolbar-shortcuts'

describe('useToolbarShortcuts', () => {
  let callbacks: ToolbarShortcutCallbacks

  beforeEach(() => {
    callbacks = {
      onCreateAbove: vi.fn(),
      onCreateBelow: vi.fn(),
      onUndo: vi.fn(),
      onRedo: vi.fn(),
      onOpenAgentLauncher: vi.fn(),
      onDecreaseDepth: vi.fn(),
      onIncreaseDepth: vi.fn(),
      onFocusSearch: vi.fn(),
      onNextMatch: vi.fn(),
      onPreviousMatch: vi.fn(),
      onEmbedSearch: vi.fn(),
    }
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  function dispatchKeyDown(key: string, options: Partial<KeyboardEvent> = {}) {
    const event = new KeyboardEvent('keydown', {
      key,
      bubbles: true,
      ...options,
    })
    document.dispatchEvent(event)
  }

  it('calls onCreateAbove when Shift+O is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('O', { shiftKey: true })
    expect(callbacks.onCreateAbove).toHaveBeenCalled()
  })

  it('calls onCreateBelow when O is pressed (without shift)', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('o')
    expect(callbacks.onCreateBelow).toHaveBeenCalled()
  })

  it('calls onUndo when u is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('u')
    expect(callbacks.onUndo).toHaveBeenCalled()
  })

  it('calls onUndo when Ctrl+Z is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('z', { ctrlKey: true })
    expect(callbacks.onUndo).toHaveBeenCalled()
  })

  it('calls onUndo when Cmd+Z is pressed (Mac)', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('z', { metaKey: true })
    expect(callbacks.onUndo).toHaveBeenCalled()
  })

  it('does not call onUndo when Ctrl+Z is pressed and canUndo is false', () => {
    const callbacksWithDisabled = {
      ...callbacks,
      canUndo: false,
    }
    renderHook(() => useToolbarShortcuts(callbacksWithDisabled))

    dispatchKeyDown('z', { ctrlKey: true })
    expect(callbacksWithDisabled.onUndo).not.toHaveBeenCalled()
  })

  it('calls onRedo when Ctrl+Shift+Z is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('z', { ctrlKey: true, shiftKey: true })
    expect(callbacks.onRedo).toHaveBeenCalled()
  })

  it('calls onOpenAgentLauncher when e is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('e')
    expect(callbacks.onOpenAgentLauncher).toHaveBeenCalled()
  })

  it('calls onDecreaseDepth when [ is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('[')
    expect(callbacks.onDecreaseDepth).toHaveBeenCalled()
  })

  it('calls onIncreaseDepth when ] is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown(']')
    expect(callbacks.onIncreaseDepth).toHaveBeenCalled()
  })

  it('calls onFocusSearch when / is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('/')
    expect(callbacks.onFocusSearch).toHaveBeenCalled()
  })

  it('calls onNextMatch when n is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('n')
    expect(callbacks.onNextMatch).toHaveBeenCalled()
  })

  it('calls onPreviousMatch when N (Shift+n) is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('N', { shiftKey: true })
    expect(callbacks.onPreviousMatch).toHaveBeenCalled()
  })

  it('does not trigger shortcuts when typing in input fields', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()

    // Create and dispatch event from input element
    const event = new KeyboardEvent('keydown', {
      key: 'o',
      bubbles: true,
    })
    Object.defineProperty(event, 'target', { value: input })
    document.dispatchEvent(event)

    expect(callbacks.onCreateBelow).not.toHaveBeenCalled()

    document.body.removeChild(input)
  })

  it('does not trigger shortcuts when typing in textarea', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    const textarea = document.createElement('textarea')
    document.body.appendChild(textarea)
    textarea.focus()

    const event = new KeyboardEvent('keydown', {
      key: 'u',
      bubbles: true,
    })
    Object.defineProperty(event, 'target', { value: textarea })
    document.dispatchEvent(event)

    expect(callbacks.onUndo).not.toHaveBeenCalled()

    document.body.removeChild(textarea)
  })

  it('cleans up event listener on unmount', () => {
    const removeEventListenerSpy = vi.spyOn(document, 'removeEventListener')
    const { unmount } = renderHook(() => useToolbarShortcuts(callbacks))

    unmount()

    expect(removeEventListenerSpy).toHaveBeenCalledWith('keydown', expect.any(Function))
    removeEventListenerSpy.mockRestore()
  })

  it('does not call disabled callbacks', () => {
    const callbacksWithDisabled = {
      ...callbacks,
      onUndo: vi.fn(),
      canUndo: false,
    }

    renderHook(() => useToolbarShortcuts(callbacksWithDisabled))

    dispatchKeyDown('u')
    expect(callbacksWithDisabled.onUndo).not.toHaveBeenCalled()
  })

  it('calls enabled callbacks', () => {
    const callbacksWithEnabled = {
      ...callbacks,
      onUndo: vi.fn(),
      canUndo: true,
    }

    renderHook(() => useToolbarShortcuts(callbacksWithEnabled))

    dispatchKeyDown('u')
    expect(callbacksWithEnabled.onUndo).toHaveBeenCalled()
  })

  it('calls onToggleFilter when f is pressed', () => {
    const onToggleFilter = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onToggleFilter }))

    dispatchKeyDown('f')
    expect(onToggleFilter).toHaveBeenCalled()
  })

  it('does not call onToggleFilter when f is pressed with shift', () => {
    const onToggleFilter = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onToggleFilter }))

    dispatchKeyDown('f', { shiftKey: true })
    expect(onToggleFilter).not.toHaveBeenCalled()
  })

  it('does not call onToggleFilter when f is pressed with ctrl', () => {
    const onToggleFilter = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onToggleFilter }))

    dispatchKeyDown('f', { ctrlKey: true })
    expect(onToggleFilter).not.toHaveBeenCalled()
  })

  it('does nothing when onToggleFilter is undefined and f is pressed', () => {
    // Just ensure it doesn't throw
    renderHook(() => useToolbarShortcuts(callbacks))
    expect(() => dispatchKeyDown('f')).not.toThrow()
  })
})
