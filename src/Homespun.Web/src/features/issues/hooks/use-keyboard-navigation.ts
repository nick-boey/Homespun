import { useCallback, useRef, useState } from 'react'
import { IssueType } from '@/api'
import {
  KeyboardEditMode,
  EditCursorPosition,
  TYPE_CYCLE_DEBOUNCE_MS,
  type InlineEditState,
  type PendingNewIssue,
  type SearchState,
  type TaskGraphRenderLine,
} from '../types'
import { useIssueSelection, type UseIssueSelectionReturn } from './use-issue-selection'

export interface UseKeyboardNavigationOptions {
  /** The project ID for API operations. */
  projectId: string
  /** Callback fired when any navigation or edit state changes. */
  onStateChanged?: () => void
  /** Callback fired when Enter is pressed in Viewing mode to open full edit. */
  onOpenEditRequested?: (issueId: string) => void
  /** Callback fired when an issue is created or updated. */
  onIssueChanged?: () => Promise<void>
  /** Callback fired when an issue is created and should be opened for description editing. */
  onIssueCreatedForEdit?: (issueId: string) => Promise<void>
  /** Callback fired when type cycling should be performed on the selected issue. */
  onCycleIssueType?: (issueId: string, currentType: IssueType) => Promise<void>
  /** Callback fired when a sibling move is requested (J/K keys). */
  onSiblingMoveRequested?: (issueId: string, direction: 'Up' | 'Down') => Promise<void>
}

export interface UseKeyboardNavigationReturn {
  // Selection state (delegated)
  selectedIndex: number
  selectedIssueId: string | undefined
  selection: UseIssueSelectionReturn

  // Edit mode state
  editMode: KeyboardEditMode
  pendingEdit: InlineEditState | null
  pendingNewIssue: PendingNewIssue | null
  selectedPromptIndex: number

  // Search state
  search: SearchState

  // Initialization
  initialize: (renderLines: TaskGraphRenderLine[]) => void

  // Navigation methods (only work in Viewing mode)
  moveUp: () => void
  moveDown: () => void
  moveToParent: () => void
  moveToChild: () => void
  moveToFirst: () => void
  moveToLast: () => void

  // Editing methods
  startEditingAtStart: () => void
  startEditingAtEnd: () => void
  startReplacingTitle: () => void
  updateEditTitle: (title: string) => void
  cancelEdit: () => void
  acceptEditAsync: () => Promise<void>
  acceptEditAndOpenDescriptionAsync: () => Promise<void>

  // Issue creation
  createIssueBelow: () => void
  createIssueAbove: () => void
  indentAsChild: () => void
  unindentAsSibling: () => void

  // Agent prompt selection
  startSelectingPrompt: () => void
  movePromptSelectionDown: () => void
  movePromptSelectionUp: () => void
  acceptPromptSelection: () => void

  // Search methods
  startSearch: () => void
  updateSearchTerm: (term: string) => void
  embedSearch: () => void
  moveToNextMatch: () => void
  moveToPreviousMatch: () => void
  clearSearch: () => void

  // Other
  openSelectedIssueForEdit: () => void
  cycleIssueTypeAsync: () => Promise<void>
}

const initialSearchState: SearchState = {
  searchTerm: '',
  isSearching: false,
  isSearchEmbedded: false,
  matchingIndices: [],
  currentMatchIndex: -1,
}

/**
 * Hook for Vim-like keyboard navigation in the task graph.
 * Manages edit modes, selection, search, and issue operations.
 */
export function useKeyboardNavigation(
  options: UseKeyboardNavigationOptions
): UseKeyboardNavigationReturn {
  const {
    onStateChanged,
    onOpenEditRequested,
    onIssueChanged,
    onIssueCreatedForEdit,
    onCycleIssueType,
  } = options

  // Render lines state - stored to avoid issues with React Compiler
  const [renderLines, setRenderLinesInternal] = useState<TaskGraphRenderLine[]>([])

  // Selection state (delegated to useIssueSelection)
  const selection = useIssueSelection({
    onSelectionChange: () => {
      onStateChanged?.()
    },
  })

  // Edit mode state
  const [editMode, setEditMode] = useState<KeyboardEditMode>(KeyboardEditMode.Viewing)
  const [pendingEdit, setPendingEdit] = useState<InlineEditState | null>(null)
  const [pendingNewIssue, setPendingNewIssue] = useState<PendingNewIssue | null>(null)
  const [selectedPromptIndex, setSelectedPromptIndex] = useState(0)

  // Search state
  const [searchState, setSearchState] = useState<SearchState>(initialSearchState)

  // Type cycling debounce - this ref is only accessed in event handlers, not during render
  const lastTypeCycleTimeRef = useRef<Map<string, number>>(new Map())

  // ============================================================================
  // Initialization
  // ============================================================================

  const initialize = useCallback(
    (lines: TaskGraphRenderLine[]) => {
      setRenderLinesInternal(lines)
      selection.setRenderLines(lines)
      // Pass lines directly to avoid React state batching issues
      selection.selectFirstActionable(lines)

      // Reset edit state
      setEditMode(KeyboardEditMode.Viewing)
      setPendingEdit(null)
      setPendingNewIssue(null)

      // Reset search state
      setSearchState(initialSearchState)

      onStateChanged?.()
    },
    [selection, onStateChanged]
  )

  // ============================================================================
  // Navigation (only work in Viewing mode and not searching)
  // ============================================================================

  const moveUp = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing || searchState.isSearching) return
    selection.moveUp()
  }, [editMode, searchState.isSearching, selection])

  const moveDown = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing || searchState.isSearching) return
    selection.moveDown()
  }, [editMode, searchState.isSearching, selection])

  const moveToParent = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing || searchState.isSearching) return
    selection.moveToParent()
  }, [editMode, searchState.isSearching, selection])

  const moveToChild = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing || searchState.isSearching) return
    selection.moveToChild()
  }, [editMode, searchState.isSearching, selection])

  const moveToFirst = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing || searchState.isSearching) return
    selection.moveToFirst()
  }, [editMode, searchState.isSearching, selection])

  const moveToLast = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing || searchState.isSearching) return
    selection.moveToLast()
  }, [editMode, searchState.isSearching, selection])

  // ============================================================================
  // Editing
  // ============================================================================

  const startEditingAtStart = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing) return
    const renderLine = selection.getSelectedRenderLine()
    if (!renderLine) return

    setPendingEdit({
      issueId: renderLine.issueId,
      title: renderLine.title,
      originalTitle: renderLine.title,
      cursorPosition: EditCursorPosition.Start,
    })
    setEditMode(KeyboardEditMode.EditingExisting)
    onStateChanged?.()
  }, [editMode, selection, onStateChanged])

  const startEditingAtEnd = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing) return
    const renderLine = selection.getSelectedRenderLine()
    if (!renderLine) return

    setPendingEdit({
      issueId: renderLine.issueId,
      title: renderLine.title,
      originalTitle: renderLine.title,
      cursorPosition: EditCursorPosition.End,
    })
    setEditMode(KeyboardEditMode.EditingExisting)
    onStateChanged?.()
  }, [editMode, selection, onStateChanged])

  const startReplacingTitle = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing) return
    const renderLine = selection.getSelectedRenderLine()
    if (!renderLine) return

    setPendingEdit({
      issueId: renderLine.issueId,
      title: '',
      originalTitle: renderLine.title,
      cursorPosition: EditCursorPosition.Replace,
    })
    setEditMode(KeyboardEditMode.EditingExisting)
    onStateChanged?.()
  }, [editMode, selection, onStateChanged])

  const updateEditTitle = useCallback(
    (title: string) => {
      if (editMode === KeyboardEditMode.EditingExisting && pendingEdit) {
        setPendingEdit({ ...pendingEdit, title })
      } else if (editMode === KeyboardEditMode.CreatingNew && pendingNewIssue) {
        setPendingNewIssue({ ...pendingNewIssue, title })
      }
    },
    [editMode, pendingEdit, pendingNewIssue]
  )

  const cancelEdit = useCallback(() => {
    setEditMode(KeyboardEditMode.Viewing)
    setPendingEdit(null)
    setPendingNewIssue(null)
    setSelectedPromptIndex(0)
    setSearchState(initialSearchState)
    onStateChanged?.()
  }, [onStateChanged])

  const acceptEditAsync = useCallback(async () => {
    setEditMode(KeyboardEditMode.Viewing)
    setPendingEdit(null)
    setPendingNewIssue(null)
    onStateChanged?.()
    await onIssueChanged?.()
  }, [onStateChanged, onIssueChanged])

  const acceptEditAndOpenDescriptionAsync = useCallback(async () => {
    const issueId = pendingEdit?.issueId ?? pendingNewIssue?.referenceIssueId
    setEditMode(KeyboardEditMode.Viewing)
    setPendingEdit(null)
    setPendingNewIssue(null)
    onStateChanged?.()
    await onIssueChanged?.()
    if (issueId) {
      await onIssueCreatedForEdit?.(issueId)
    }
  }, [pendingEdit, pendingNewIssue, onStateChanged, onIssueChanged, onIssueCreatedForEdit])

  // ============================================================================
  // Issue Creation
  // ============================================================================

  const createIssueBelow = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing) return
    if (selection.selectedIndex < 0) return

    const referenceIssueId =
      selection.selectedIndex < renderLines.length
        ? renderLines[selection.selectedIndex].issueId
        : undefined

    setPendingNewIssue({
      insertAtIndex: selection.selectedIndex + 1,
      title: '',
      isAbove: false,
      referenceIssueId,
    })
    setEditMode(KeyboardEditMode.CreatingNew)
    onStateChanged?.()
  }, [editMode, selection.selectedIndex, renderLines, onStateChanged])

  const createIssueAbove = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing) return
    if (selection.selectedIndex < 0) return

    const referenceIssueId =
      selection.selectedIndex < renderLines.length
        ? renderLines[selection.selectedIndex].issueId
        : undefined

    setPendingNewIssue({
      insertAtIndex: selection.selectedIndex,
      title: '',
      isAbove: true,
      referenceIssueId,
    })
    setEditMode(KeyboardEditMode.CreatingNew)
    onStateChanged?.()
  }, [editMode, selection.selectedIndex, renderLines, onStateChanged])

  const indentAsChild = useCallback(() => {
    if (editMode === KeyboardEditMode.CreatingNew && pendingNewIssue) {
      if (pendingNewIssue.referenceIssueId) {
        setPendingNewIssue({
          ...pendingNewIssue,
          pendingChildId: pendingNewIssue.referenceIssueId,
          pendingParentId: undefined,
          inheritedParentIssueId: undefined,
          inheritedParentSortOrder: undefined,
        })
        onStateChanged?.()
      }
    }
  }, [editMode, pendingNewIssue, onStateChanged])

  const unindentAsSibling = useCallback(() => {
    if (editMode === KeyboardEditMode.CreatingNew && pendingNewIssue) {
      if (pendingNewIssue.referenceIssueId) {
        setPendingNewIssue({
          ...pendingNewIssue,
          pendingParentId: pendingNewIssue.referenceIssueId,
          pendingChildId: undefined,
          inheritedParentIssueId: undefined,
          inheritedParentSortOrder: undefined,
        })
        onStateChanged?.()
      }
    }
  }, [editMode, pendingNewIssue, onStateChanged])

  // ============================================================================
  // Agent Prompt Selection
  // ============================================================================

  const startSelectingPrompt = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing) return
    if (selection.selectedIndex < 0) return

    setEditMode(KeyboardEditMode.SelectingAgentPrompt)
    setSelectedPromptIndex(0)
    onStateChanged?.()
  }, [editMode, selection.selectedIndex, onStateChanged])

  const movePromptSelectionDown = useCallback(() => {
    if (editMode !== KeyboardEditMode.SelectingAgentPrompt) return
    setSelectedPromptIndex((prev) => prev + 1)
    onStateChanged?.()
  }, [editMode, onStateChanged])

  const movePromptSelectionUp = useCallback(() => {
    if (editMode !== KeyboardEditMode.SelectingAgentPrompt) return
    if (selectedPromptIndex <= 0) return
    setSelectedPromptIndex((prev) => prev - 1)
    onStateChanged?.()
  }, [editMode, selectedPromptIndex, onStateChanged])

  const acceptPromptSelection = useCallback(() => {
    if (editMode !== KeyboardEditMode.SelectingAgentPrompt) return
    setEditMode(KeyboardEditMode.Viewing)
    setSelectedPromptIndex(0)
    onStateChanged?.()
  }, [editMode, onStateChanged])

  // ============================================================================
  // Search
  // ============================================================================

  const computeMatchingIndices = useCallback(
    (term: string): number[] => {
      if (!term) return []
      const matches: number[] = []
      for (let i = 0; i < renderLines.length; i++) {
        if (renderLines[i].title.toLowerCase().includes(term.toLowerCase())) {
          matches.push(i)
        }
      }
      return matches
    },
    [renderLines]
  )

  const startSearch = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing) return

    setSearchState({
      searchTerm: '',
      isSearching: true,
      isSearchEmbedded: false,
      matchingIndices: [],
      currentMatchIndex: -1,
    })
    onStateChanged?.()
  }, [editMode, onStateChanged])

  const updateSearchTerm = useCallback(
    (term: string) => {
      if (!searchState.isSearching) return

      const matches = computeMatchingIndices(term)
      setSearchState((prev) => ({
        ...prev,
        searchTerm: term,
        matchingIndices: matches,
      }))
      onStateChanged?.()
    },
    [searchState.isSearching, computeMatchingIndices, onStateChanged]
  )

  const embedSearch = useCallback(() => {
    if (!searchState.isSearching) return

    if (searchState.matchingIndices.length > 0 && renderLines.length > 0) {
      const firstMatchIndex = searchState.matchingIndices[0]
      if (firstMatchIndex < renderLines.length) {
        selection.selectIssue(renderLines[firstMatchIndex].issueId)
      }
      setSearchState((prev) => ({
        ...prev,
        isSearching: false,
        isSearchEmbedded: true,
        currentMatchIndex: 0,
      }))
    } else {
      setSearchState((prev) => ({
        ...prev,
        isSearching: false,
        isSearchEmbedded: true,
      }))
    }
    onStateChanged?.()
  }, [searchState.isSearching, searchState.matchingIndices, renderLines, selection, onStateChanged])

  const moveToNextMatch = useCallback(() => {
    if (!searchState.isSearchEmbedded || searchState.matchingIndices.length === 0) return

    const nextIndex = (searchState.currentMatchIndex + 1) % searchState.matchingIndices.length
    const nextIssueIndex = searchState.matchingIndices[nextIndex]
    if (nextIssueIndex < renderLines.length) {
      selection.selectIssue(renderLines[nextIssueIndex].issueId)
    }

    setSearchState((prev) => ({
      ...prev,
      currentMatchIndex: nextIndex,
    }))
    onStateChanged?.()
  }, [
    searchState.isSearchEmbedded,
    searchState.matchingIndices,
    searchState.currentMatchIndex,
    renderLines,
    selection,
    onStateChanged,
  ])

  const moveToPreviousMatch = useCallback(() => {
    if (!searchState.isSearchEmbedded || searchState.matchingIndices.length === 0) return

    const prevIndex =
      (searchState.currentMatchIndex - 1 + searchState.matchingIndices.length) %
      searchState.matchingIndices.length
    const prevIssueIndex = searchState.matchingIndices[prevIndex]
    if (prevIssueIndex < renderLines.length) {
      selection.selectIssue(renderLines[prevIssueIndex].issueId)
    }

    setSearchState((prev) => ({
      ...prev,
      currentMatchIndex: prevIndex,
    }))
    onStateChanged?.()
  }, [
    searchState.isSearchEmbedded,
    searchState.matchingIndices,
    searchState.currentMatchIndex,
    renderLines,
    selection,
    onStateChanged,
  ])

  const clearSearch = useCallback(() => {
    setSearchState(initialSearchState)
    onStateChanged?.()
  }, [onStateChanged])

  // ============================================================================
  // Other
  // ============================================================================

  const openSelectedIssueForEdit = useCallback(() => {
    if (editMode !== KeyboardEditMode.Viewing) return
    const issueId = selection.selectedIssueId
    if (!issueId) return
    onOpenEditRequested?.(issueId)
  }, [editMode, selection.selectedIssueId, onOpenEditRequested])

  const cycleIssueTypeAsync = useCallback(async () => {
    if (editMode !== KeyboardEditMode.Viewing) return
    const renderLine = selection.getSelectedRenderLine()
    if (!renderLine) return

    const now = Date.now()
    const lastTime = lastTypeCycleTimeRef.current.get(renderLine.issueId)
    if (lastTime && now - lastTime < TYPE_CYCLE_DEBOUNCE_MS) {
      return // Debounced
    }

    lastTypeCycleTimeRef.current.set(renderLine.issueId, now)
    await onCycleIssueType?.(renderLine.issueId, renderLine.issueType)
  }, [editMode, selection, onCycleIssueType])

  return {
    // Selection state
    selectedIndex: selection.selectedIndex,
    selectedIssueId: selection.selectedIssueId,
    selection,

    // Edit mode state
    editMode,
    pendingEdit,
    pendingNewIssue,
    selectedPromptIndex,

    // Search state
    search: searchState,

    // Initialization
    initialize,

    // Navigation methods
    moveUp,
    moveDown,
    moveToParent,
    moveToChild,
    moveToFirst,
    moveToLast,

    // Editing methods
    startEditingAtStart,
    startEditingAtEnd,
    startReplacingTitle,
    updateEditTitle,
    cancelEdit,
    acceptEditAsync,
    acceptEditAndOpenDescriptionAsync,

    // Issue creation
    createIssueBelow,
    createIssueAbove,
    indentAsChild,
    unindentAsSibling,

    // Agent prompt selection
    startSelectingPrompt,
    movePromptSelectionDown,
    movePromptSelectionUp,
    acceptPromptSelection,

    // Search methods
    startSearch,
    updateSearchTerm,
    embedSearch,
    moveToNextMatch,
    moveToPreviousMatch,
    clearSearch,

    // Other
    openSelectedIssueForEdit,
    cycleIssueTypeAsync,
  }
}
