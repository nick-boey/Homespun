import { useEffect, useCallback } from 'react'

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
  onToggleFilter?: () => void
  canUndo?: boolean
  canRedo?: boolean
}

function isInputElement(element: EventTarget | null): boolean {
  if (!element || !(element instanceof HTMLElement)) return false
  const tagName = element.tagName.toLowerCase()
  return (
    tagName === 'input' ||
    tagName === 'textarea' ||
    tagName === 'select' ||
    element.isContentEditable
  )
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
    onToggleFilter,
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
      if (!shiftKey && !ctrlKey && !metaKey && key === 'f') {
        if (onToggleFilter) {
          event.preventDefault()
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
      onOpenAgentLauncher,
      onDecreaseDepth,
      onIncreaseDepth,
      onFocusSearch,
      onNextMatch,
      onPreviousMatch,
      onToggleFilter,
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
