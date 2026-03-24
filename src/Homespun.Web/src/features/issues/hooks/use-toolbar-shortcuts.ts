import { useEffect, useCallback } from 'react'
import { isInputElement } from '@/lib/utils/is-input-element'

export interface ToolbarShortcutCallbacks {
  onCreateAbove: () => void
  onCreateBelow: () => void
  onUndo: () => void
  onRedo: () => void
  onOpenAgentLauncher: () => void
  onDecreaseDepth: () => void
  onIncreaseDepth: () => void
  onFocusSearch: () => void
  onNextMatch: () => void
  onPreviousMatch: () => void
  onEmbedSearch: () => void
  onMoveUp?: () => void
  onMoveDown?: () => void
  canMoveUp?: boolean
  canMoveDown?: boolean
  onToggleFilter?: () => void
  /** Whether the filter panel is currently active/visible */
  isFilterActive?: boolean
  /** Focus the filter input with cursor at the end */
  onFocusFilterAtEnd?: () => void
  canUndo?: boolean
  canRedo?: boolean
}

export function useToolbarShortcuts(callbacks: ToolbarShortcutCallbacks) {
  const {
    onCreateAbove,
    onCreateBelow,
    onUndo,
    onRedo,
    onOpenAgentLauncher,
    onDecreaseDepth,
    onIncreaseDepth,
    onFocusSearch,
    onNextMatch,
    onPreviousMatch,
    onMoveUp,
    onMoveDown,
    canMoveUp = false,
    canMoveDown = false,
    onToggleFilter,
    isFilterActive = false,
    onFocusFilterAtEnd,
    canUndo = true,
    canRedo = true,
  } = callbacks

  const handleKeyDown = useCallback(
    (event: KeyboardEvent) => {
      // Don't trigger shortcuts when typing in input fields
      if (isInputElement(event.target)) {
        return
      }

      const { key, shiftKey, ctrlKey, metaKey } = event

      // Move up: Ctrl+Shift+ArrowUp or Cmd+Shift+ArrowUp
      if ((ctrlKey || metaKey) && shiftKey && key === 'ArrowUp') {
        if (canMoveUp && onMoveUp) {
          event.preventDefault()
          onMoveUp()
        }
        return
      }

      // Move down: Ctrl+Shift+ArrowDown or Cmd+Shift+ArrowDown
      if ((ctrlKey || metaKey) && shiftKey && key === 'ArrowDown') {
        if (canMoveDown && onMoveDown) {
          event.preventDefault()
          onMoveDown()
        }
        return
      }

      // Redo: Ctrl+Shift+Z or Cmd+Shift+Z
      if ((ctrlKey || metaKey) && shiftKey && key.toLowerCase() === 'z') {
        if (canRedo) {
          event.preventDefault()
          onRedo()
        }
        return
      }

      // Undo: Ctrl+Z or Cmd+Z
      if ((ctrlKey || metaKey) && !shiftKey && key.toLowerCase() === 'z') {
        if (canUndo) {
          event.preventDefault()
          onUndo()
        }
        return
      }

      // Create above: Shift+O
      if (shiftKey && key === 'O') {
        event.preventDefault()
        onCreateAbove()
        return
      }

      // Create below: O (without shift)
      if (!shiftKey && !ctrlKey && !metaKey && key.toLowerCase() === 'o') {
        event.preventDefault()
        onCreateBelow()
        return
      }

      // Undo: u
      if (!shiftKey && !ctrlKey && !metaKey && key === 'u') {
        if (canUndo) {
          event.preventDefault()
          onUndo()
        }
        return
      }

      // Agent launcher: e
      if (!shiftKey && !ctrlKey && !metaKey && key === 'e') {
        event.preventDefault()
        onOpenAgentLauncher()
        return
      }

      // Decrease depth: [
      if (key === '[') {
        event.preventDefault()
        onDecreaseDepth()
        return
      }

      // Increase depth: ]
      if (key === ']') {
        event.preventDefault()
        onIncreaseDepth()
        return
      }

      // Focus search: /
      if (key === '/') {
        event.preventDefault()
        onFocusSearch()
        return
      }

      // Previous match: Shift+N
      if (shiftKey && key === 'N') {
        event.preventDefault()
        onPreviousMatch()
        return
      }

      // Next match: n
      if (!shiftKey && !ctrlKey && !metaKey && key === 'n') {
        event.preventDefault()
        onNextMatch()
        return
      }

      // Toggle filter: f
      // If filter is already open, focus input at end; otherwise toggle
      if (!shiftKey && !ctrlKey && !metaKey && key === 'f') {
        event.preventDefault()
        if (isFilterActive && onFocusFilterAtEnd) {
          onFocusFilterAtEnd()
        } else if (onToggleFilter) {
          onToggleFilter()
        }
        return
      }
    },
    [
      onCreateAbove,
      onCreateBelow,
      onUndo,
      onRedo,
      onMoveUp,
      onMoveDown,
      canMoveUp,
      canMoveDown,
      onOpenAgentLauncher,
      onDecreaseDepth,
      onIncreaseDepth,
      onFocusSearch,
      onNextMatch,
      onPreviousMatch,
      onToggleFilter,
      isFilterActive,
      onFocusFilterAtEnd,
      canUndo,
      canRedo,
    ]
  )

  useEffect(() => {
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [handleKeyDown])
}
