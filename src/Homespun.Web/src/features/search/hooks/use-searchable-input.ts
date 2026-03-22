import { useState, useCallback, useMemo, useRef, useEffect } from 'react'
import { useMentionTrigger, type MentionTriggerState } from './use-mention-trigger'
import { useProjectFiles } from './use-project-files'
import { useSearchablePrs } from './use-searchable-prs'
import type { MentionSelection } from '../components/mention-search-popup'

export interface UseSearchableInputOptions {
  /** Project ID for fetching files and PRs */
  projectId: string
  /** Current input value */
  value: string
  /** Callback when value changes */
  onChange: (value: string) => void
}

export interface UseSearchableInputResult {
  /** Ref to attach to the textarea */
  inputRef: React.RefObject<HTMLTextAreaElement | HTMLInputElement | null>
  /** Current trigger state */
  triggerState: MentionTriggerState
  /** Files for search */
  files: string[]
  /** PRs for search */
  prs: ReturnType<typeof useSearchablePrs>['prs']
  /** Whether files are loading */
  isLoadingFiles: boolean
  /** Whether PRs are loading */
  isLoadingPrs: boolean
  /** Whether the search popup should be open */
  isSearchOpen: boolean
  /** Handle cursor position change (call on selection change, click, keyup) */
  handleCursorChange: () => void
  /** Handle selection of an item from the popup */
  handleSelect: (selection: MentionSelection) => void
  /** Close the search popup */
  closeSearch: () => void
}

/**
 * Hook that provides searchable input functionality for @ and # mentions.
 * Tracks cursor position, detects triggers, and handles selection insertion.
 */
export function useSearchableInput({
  projectId,
  value,
  onChange,
}: UseSearchableInputOptions): UseSearchableInputResult {
  const inputRef = useRef<HTMLTextAreaElement | HTMLInputElement | null>(null)
  const [cursorPosition, setCursorPosition] = useState(0)
  const [isManuallyHidden, setIsManuallyHidden] = useState(false)

  // Fetch data
  const { files, isLoading: isLoadingFiles } = useProjectFiles(projectId)
  const { prs, isLoading: isLoadingPrs } = useSearchablePrs(projectId)

  // Detect trigger
  const triggerState = useMentionTrigger(value, cursorPosition)

  // Compute whether popup should be open
  const isSearchOpen = useMemo(() => {
    return triggerState.active && !isManuallyHidden
  }, [triggerState.active, isManuallyHidden])

  // Reset manual hide when trigger position changes
  // This is a valid pattern for resetting state based on derived values
  const prevTriggerPosRef = useRef<number>(-1)
  useEffect(() => {
    if (triggerState.triggerPosition !== prevTriggerPosRef.current && triggerState.active) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setIsManuallyHidden(false)
    }
    prevTriggerPosRef.current = triggerState.triggerPosition
  }, [triggerState.triggerPosition, triggerState.active])

  // Handle cursor position changes
  const handleCursorChange = useCallback(() => {
    if (inputRef.current) {
      setCursorPosition(inputRef.current.selectionStart ?? 0)
    }
  }, [])

  // Close search popup
  const closeSearch = useCallback(() => {
    setIsManuallyHidden(true)
  }, [])

  // Handle item selection
  const handleSelect = useCallback(
    (selection: MentionSelection) => {
      if (!triggerState.active) return

      const { triggerPosition, query } = triggerState
      const beforeTrigger = value.slice(0, triggerPosition)
      const afterQuery = value.slice(triggerPosition + 1 + query.length)

      // Format the insertion based on type
      let insertion: string
      if (selection.type === '@') {
        // Use quotes only if the file path contains spaces
        const hasSpaces = selection.value.includes(' ')
        insertion = hasSpaces ? `@"${selection.value}"` : `@${selection.value}`
      } else {
        insertion = `PR #${selection.value}`
      }

      const newValue = beforeTrigger + insertion + afterQuery
      onChange(newValue)

      // Move cursor to end of insertion
      const newCursorPos = triggerPosition + insertion.length
      requestAnimationFrame(() => {
        if (inputRef.current) {
          inputRef.current.setSelectionRange(newCursorPos, newCursorPos)
          inputRef.current.focus()
          setCursorPosition(newCursorPos)
        }
      })

      // Close the popup
      setIsManuallyHidden(true)
    },
    [triggerState, value, onChange]
  )

  return {
    inputRef,
    triggerState,
    files: files ?? [],
    prs: prs ?? [],
    isLoadingFiles,
    isLoadingPrs,
    isSearchOpen,
    handleCursorChange,
    handleSelect,
    closeSearch,
  }
}
