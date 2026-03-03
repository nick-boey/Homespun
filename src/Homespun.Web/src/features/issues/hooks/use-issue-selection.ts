import { useCallback, useMemo, useState } from 'react'
import type { TaskGraphRenderLine } from '../types'

export interface UseIssueSelectionOptions {
  /**
   * Callback fired when the selection changes.
   */
  onSelectionChange?: (issueId: string | undefined, index: number) => void
}

export interface UseIssueSelectionReturn {
  /** Current selected index in the render list. -1 if no selection. */
  selectedIndex: number
  /** ID of the currently selected issue, or undefined if none selected. */
  selectedIssueId: string | undefined
  /** Set the render lines for navigation. */
  setRenderLines: (lines: TaskGraphRenderLine[]) => void
  /**
   * Select the first actionable issue, or first issue if none actionable.
   * @param lines Optional render lines to use (for synchronous initialization).
   *              If not provided, uses internal state.
   */
  selectFirstActionable: (lines?: TaskGraphRenderLine[]) => void
  /** Select an issue by its ID. */
  selectIssue: (issueId: string) => void
  /** Clear the current selection. */
  clearSelection: () => void
  /** Move selection up one issue (k / ArrowUp). */
  moveUp: () => void
  /** Move selection down one issue (j / ArrowDown). */
  moveDown: () => void
  /** Navigate to parent issue (h / ArrowLeft). */
  moveToParent: () => void
  /** Navigate to first child issue (l / ArrowRight). */
  moveToChild: () => void
  /** Jump to first issue (g). */
  moveToFirst: () => void
  /** Jump to last issue (G). */
  moveToLast: () => void
  /** Get the currently selected render line, or undefined if no selection. */
  getSelectedRenderLine: () => TaskGraphRenderLine | undefined
}

/**
 * Hook for managing issue selection state in the task graph.
 * Provides navigation methods for Vim-like keyboard navigation.
 */
export function useIssueSelection(options: UseIssueSelectionOptions = {}): UseIssueSelectionReturn {
  const { onSelectionChange } = options

  const [selectedIndex, setSelectedIndex] = useState(-1)
  const [renderLines, setRenderLinesState] = useState<TaskGraphRenderLine[]>([])

  const setRenderLines = useCallback((lines: TaskGraphRenderLine[]) => {
    setRenderLinesState(lines)
  }, [])

  const setSelectionAndNotify = useCallback(
    (index: number, lines: TaskGraphRenderLine[]) => {
      setSelectedIndex(index)
      const issueId = index >= 0 && index < lines.length ? lines[index].issueId : undefined
      onSelectionChange?.(issueId, index)
    },
    [onSelectionChange]
  )

  const selectFirstActionable = useCallback(
    (lines?: TaskGraphRenderLine[]) => {
      const effectiveLines = lines ?? renderLines
      if (effectiveLines.length === 0) return

      // Try to find an actionable issue first
      for (let i = 0; i < effectiveLines.length; i++) {
        if (effectiveLines[i].isActionable) {
          setSelectionAndNotify(i, effectiveLines)
          return
        }
      }

      // Fall back to first issue if none are actionable
      setSelectionAndNotify(0, effectiveLines)
    },
    [renderLines, setSelectionAndNotify]
  )

  const selectIssue = useCallback(
    (issueId: string) => {
      for (let i = 0; i < renderLines.length; i++) {
        if (renderLines[i].issueId === issueId) {
          setSelectionAndNotify(i, renderLines)
          return
        }
      }
    },
    [renderLines, setSelectionAndNotify]
  )

  const clearSelection = useCallback(() => {
    setSelectionAndNotify(-1, renderLines)
  }, [renderLines, setSelectionAndNotify])

  const moveUp = useCallback(() => {
    if (selectedIndex <= 0) return
    setSelectionAndNotify(selectedIndex - 1, renderLines)
  }, [selectedIndex, renderLines, setSelectionAndNotify])

  const moveDown = useCallback(() => {
    if (selectedIndex < 0) return
    if (selectedIndex >= renderLines.length - 1) return
    setSelectionAndNotify(selectedIndex + 1, renderLines)
  }, [selectedIndex, renderLines, setSelectionAndNotify])

  const moveToFirst = useCallback(() => {
    if (renderLines.length === 0) return
    setSelectionAndNotify(0, renderLines)
  }, [renderLines, setSelectionAndNotify])

  const moveToLast = useCallback(() => {
    if (renderLines.length === 0) return
    setSelectionAndNotify(renderLines.length - 1, renderLines)
  }, [renderLines, setSelectionAndNotify])

  const findIssueAtLane = useCallback(
    (lane: number): number => {
      for (let i = 0; i < renderLines.length; i++) {
        if (renderLines[i].lane === lane) return i
      }
      return -1
    },
    [renderLines]
  )

  const findChildOfLane = useCallback(
    (parentLane: number): number => {
      for (let i = 0; i < renderLines.length; i++) {
        if (renderLines[i].parentLane === parentLane) return i
      }
      return -1
    },
    [renderLines]
  )

  const moveToParent = useCallback(() => {
    if (selectedIndex < 0 || selectedIndex >= renderLines.length) return

    const currentLine = renderLines[selectedIndex]
    if (currentLine.parentLane === undefined) return

    const parentIndex = findIssueAtLane(currentLine.parentLane)
    if (parentIndex >= 0) {
      setSelectionAndNotify(parentIndex, renderLines)
    }
  }, [selectedIndex, renderLines, findIssueAtLane, setSelectionAndNotify])

  const moveToChild = useCallback(() => {
    if (selectedIndex < 0 || selectedIndex >= renderLines.length) return

    const currentLine = renderLines[selectedIndex]
    const childIndex = findChildOfLane(currentLine.lane)
    if (childIndex >= 0) {
      setSelectionAndNotify(childIndex, renderLines)
    }
  }, [selectedIndex, renderLines, findChildOfLane, setSelectionAndNotify])

  const getSelectedRenderLine = useCallback((): TaskGraphRenderLine | undefined => {
    if (selectedIndex < 0 || selectedIndex >= renderLines.length) return undefined
    return renderLines[selectedIndex]
  }, [selectedIndex, renderLines])

  const selectedIssueId = useMemo(() => {
    if (selectedIndex < 0 || selectedIndex >= renderLines.length) return undefined
    return renderLines[selectedIndex].issueId
  }, [selectedIndex, renderLines])

  return {
    selectedIndex,
    selectedIssueId,
    setRenderLines,
    selectFirstActionable,
    selectIssue,
    clearSelection,
    moveUp,
    moveDown,
    moveToParent,
    moveToChild,
    moveToFirst,
    moveToLast,
    getSelectedRenderLine,
  }
}
