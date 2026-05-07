import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useKeyboardNavigation } from './use-keyboard-navigation'
import { KeyboardEditMode, EditCursorPosition, type TaskGraphRenderLine } from '../types'
import { IssueType } from '@/api'

describe('useKeyboardNavigation', () => {
  const mockRenderLines: TaskGraphRenderLine[] = [
    {
      issueId: 'issue-1',
      title: 'First Issue',
      lane: 0,
      issueType: IssueType.TASK,
      isActionable: true,
    },
    {
      issueId: 'issue-2',
      title: 'Second Issue',
      lane: 1,
      parentLane: 0,
      issueType: IssueType.BUG,
      isActionable: false,
    },
    {
      issueId: 'issue-3',
      title: 'Third Issue',
      lane: 0,
      issueType: IssueType.TASK,
      isActionable: true,
    },
    {
      issueId: 'issue-4',
      title: 'Fourth Issue',
      lane: 2,
      parentLane: 1,
      issueType: IssueType.CHORE,
      isActionable: false,
    },
  ]

  const mockProjectId = 'test-project'

  describe('initialization', () => {
    it('initializes in Viewing mode', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      expect(result.current.editMode).toBe(KeyboardEditMode.Viewing)
    })

    it('initializes with no selection', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      expect(result.current.selectedIndex).toBe(-1)
      expect(result.current.selectedIssueId).toBeUndefined()
    })

    it('initializes with null pending edit and new issue', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      expect(result.current.pendingEdit).toBeNull()
      expect(result.current.pendingNewIssue).toBeNull()
    })

    it('allows setting render lines and selecting first actionable', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      expect(result.current.selectedIndex).toBe(0)
      expect(result.current.selectedIssueId).toBe('issue-1')
    })
  })

  describe('navigation in Viewing mode', () => {
    it('moveDown increases selection (j key)', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveDown()
      })

      expect(result.current.selectedIndex).toBe(1)
    })

    it('moveUp decreases selection (k key)', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.selection.selectIssue('issue-2')
      })

      expect(result.current.selectedIndex).toBe(1)

      act(() => {
        result.current.moveUp()
      })

      expect(result.current.selectedIndex).toBe(0)
    })

    it('moveToFirst jumps to first issue (g key)', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.selection.selectIssue('issue-4')
      })

      expect(result.current.selectedIndex).toBe(3)

      act(() => {
        result.current.moveToFirst()
      })

      expect(result.current.selectedIndex).toBe(0)
    })

    it('moveToLast jumps to last issue (G key)', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveToLast()
      })

      expect(result.current.selectedIndex).toBe(3)
    })

    it('moveToParent navigates to parent (h key)', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.selection.selectIssue('issue-2')
      })

      act(() => {
        result.current.moveToParent()
      })

      expect(result.current.selectedIssueId).toBe('issue-1')
    })

    it('moveToChild navigates to first child (l key)', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.moveToChild()
      })

      expect(result.current.selectedIssueId).toBe('issue-2')
    })

    it('navigation methods are disabled when not in Viewing mode', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startEditingAtStart()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.EditingExisting)
      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveDown()
      })

      expect(result.current.selectedIndex).toBe(0) // Unchanged
    })
  })

  describe('editing: startEditingAtStart (i key)', () => {
    it('enters EditingExisting mode with cursor at start', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startEditingAtStart()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.EditingExisting)
      expect(result.current.pendingEdit).not.toBeNull()
      expect(result.current.pendingEdit?.issueId).toBe('issue-1')
      expect(result.current.pendingEdit?.title).toBe('First Issue')
      expect(result.current.pendingEdit?.originalTitle).toBe('First Issue')
      expect(result.current.pendingEdit?.cursorPosition).toBe(EditCursorPosition.Start)
    })

    it('does nothing if no selection', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.startEditingAtStart()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.Viewing)
      expect(result.current.pendingEdit).toBeNull()
    })
  })

  describe('editing: startEditingAtEnd (a key)', () => {
    it('enters EditingExisting mode with cursor at end', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startEditingAtEnd()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.EditingExisting)
      expect(result.current.pendingEdit?.cursorPosition).toBe(EditCursorPosition.End)
    })
  })

  describe('editing: startReplacingTitle (r key)', () => {
    it('enters EditingExisting mode with cleared title', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startReplacingTitle()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.EditingExisting)
      expect(result.current.pendingEdit?.title).toBe('')
      expect(result.current.pendingEdit?.originalTitle).toBe('First Issue')
      expect(result.current.pendingEdit?.cursorPosition).toBe(EditCursorPosition.Replace)
    })
  })

  describe('editing: updateEditTitle', () => {
    it('updates the pending edit title', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startEditingAtStart()
      })

      act(() => {
        result.current.updateEditTitle('New Title')
      })

      expect(result.current.pendingEdit?.title).toBe('New Title')
    })

    it('updates pending new issue title when creating', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.createIssueBelow()
      })

      act(() => {
        result.current.updateEditTitle('New Issue Title')
      })

      expect(result.current.pendingNewIssue?.title).toBe('New Issue Title')
    })
  })

  describe('editing: cancelEdit (Escape)', () => {
    it('cancels edit and returns to Viewing mode', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startEditingAtStart()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.EditingExisting)

      act(() => {
        result.current.cancelEdit()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.Viewing)
      expect(result.current.pendingEdit).toBeNull()
    })

    it('clears search state when canceling', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSearch()
      })

      act(() => {
        result.current.updateSearchTerm('test')
      })

      expect(result.current.search.isSearching).toBe(true)

      act(() => {
        result.current.cancelEdit()
      })

      expect(result.current.search.isSearching).toBe(false)
      expect(result.current.search.searchTerm).toBe('')
    })
  })

  describe('issue creation: createIssueBelow (o key)', () => {
    it('enters CreatingNew mode below current selection', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.createIssueBelow()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.CreatingNew)
      expect(result.current.pendingNewIssue).not.toBeNull()
      expect(result.current.pendingNewIssue?.mode).toBe('sibling-below')
      expect(result.current.pendingNewIssue?.referenceIssueId).toBe('issue-1')
    })

    it('does nothing without selection', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.createIssueBelow()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.Viewing)
      expect(result.current.pendingNewIssue).toBeNull()
    })
  })

  describe('issue creation: createIssueAbove (Shift+O)', () => {
    it('enters CreatingNew mode above current selection', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.selection.selectIssue('issue-2')
      })

      act(() => {
        result.current.createIssueAbove()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.CreatingNew)
      expect(result.current.pendingNewIssue?.mode).toBe('sibling-above')
      expect(result.current.pendingNewIssue?.referenceIssueId).toBe('issue-2')
    })
  })

  describe('agent prompt selection: startSelectingPrompt (e key)', () => {
    it('enters SelectingAgentPrompt mode', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSelectingPrompt()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.SelectingAgentPrompt)
      expect(result.current.selectedPromptIndex).toBe(0)
    })

    it('does nothing without selection', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.startSelectingPrompt()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.Viewing)
    })
  })

  describe('prompt selection: movePromptSelectionDown/Up', () => {
    it('movePromptSelectionDown increases prompt index', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSelectingPrompt()
      })

      act(() => {
        result.current.movePromptSelectionDown()
      })

      expect(result.current.selectedPromptIndex).toBe(1)
    })

    it('movePromptSelectionUp decreases prompt index', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSelectingPrompt()
      })

      act(() => {
        result.current.movePromptSelectionDown()
      })

      act(() => {
        result.current.movePromptSelectionDown()
      })

      expect(result.current.selectedPromptIndex).toBe(2)

      act(() => {
        result.current.movePromptSelectionUp()
      })

      expect(result.current.selectedPromptIndex).toBe(1)
    })

    it('movePromptSelectionUp does not go below 0', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSelectingPrompt()
      })

      expect(result.current.selectedPromptIndex).toBe(0)

      act(() => {
        result.current.movePromptSelectionUp()
      })

      expect(result.current.selectedPromptIndex).toBe(0)
    })
  })

  describe('prompt selection: acceptPromptSelection', () => {
    it('returns to Viewing mode and resets prompt index', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSelectingPrompt()
      })

      act(() => {
        result.current.movePromptSelectionDown()
      })

      expect(result.current.selectedPromptIndex).toBe(1)

      act(() => {
        result.current.acceptPromptSelection()
      })

      expect(result.current.editMode).toBe(KeyboardEditMode.Viewing)
      expect(result.current.selectedPromptIndex).toBe(0)
    })
  })

  describe('search: startSearch (/ key)', () => {
    it('enters searching state', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSearch()
      })

      expect(result.current.search.isSearching).toBe(true)
      expect(result.current.search.searchTerm).toBe('')
    })

    it('does nothing if not in Viewing mode', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startEditingAtStart()
      })

      act(() => {
        result.current.startSearch()
      })

      expect(result.current.search.isSearching).toBe(false)
    })
  })

  describe('search: updateSearchTerm', () => {
    it('updates the search term and computes matching indices', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSearch()
      })

      act(() => {
        result.current.updateSearchTerm('First')
      })

      expect(result.current.search.searchTerm).toBe('First')
      expect(result.current.search.matchingIndices).toContain(0) // "First Issue"
    })
  })

  describe('search: embedSearch (Enter in search)', () => {
    it('commits search and allows navigation with n/N', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSearch()
      })

      act(() => {
        result.current.updateSearchTerm('Issue')
      })

      act(() => {
        result.current.embedSearch()
      })

      expect(result.current.search.isSearching).toBe(false)
      expect(result.current.search.isSearchEmbedded).toBe(true)
      expect(result.current.search.currentMatchIndex).toBe(0)
    })
  })

  describe('search: moveToNextMatch/moveToPreviousMatch (n/N keys)', () => {
    it('moveToNextMatch cycles through matches', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSearch()
      })

      act(() => {
        result.current.updateSearchTerm('Issue')
      })

      act(() => {
        result.current.embedSearch()
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveToNextMatch()
      })

      expect(result.current.search.currentMatchIndex).toBe(1)
      expect(result.current.selectedIndex).toBe(1)
    })

    it('moveToPreviousMatch cycles backwards through matches', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSearch()
      })

      act(() => {
        result.current.updateSearchTerm('Issue')
      })

      act(() => {
        result.current.embedSearch()
      })

      act(() => {
        result.current.moveToNextMatch()
      })

      act(() => {
        result.current.moveToNextMatch()
      })

      act(() => {
        result.current.moveToPreviousMatch()
      })

      expect(result.current.search.currentMatchIndex).toBe(1)
    })
  })

  describe('search: clearSearch (Escape)', () => {
    it('clears all search state', () => {
      const { result } = renderHook(() => useKeyboardNavigation({ projectId: mockProjectId }))

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.startSearch()
      })

      act(() => {
        result.current.updateSearchTerm('Issue')
      })

      act(() => {
        result.current.embedSearch()
      })

      act(() => {
        result.current.clearSearch()
      })

      expect(result.current.search.isSearching).toBe(false)
      expect(result.current.search.isSearchEmbedded).toBe(false)
      expect(result.current.search.searchTerm).toBe('')
      expect(result.current.search.matchingIndices).toEqual([])
    })
  })

  describe('callbacks', () => {
    it('calls onStateChanged when state changes', () => {
      const onStateChanged = vi.fn()
      const { result } = renderHook(() =>
        useKeyboardNavigation({ projectId: mockProjectId, onStateChanged })
      )

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      expect(onStateChanged).toHaveBeenCalled()
    })

    it('calls onOpenEditRequested when openSelectedIssueForEdit is called', () => {
      const onOpenEditRequested = vi.fn()
      const { result } = renderHook(() =>
        useKeyboardNavigation({ projectId: mockProjectId, onOpenEditRequested })
      )

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      act(() => {
        result.current.openSelectedIssueForEdit()
      })

      expect(onOpenEditRequested).toHaveBeenCalledWith('issue-1')
    })
  })

  describe('type cycling debounce', () => {
    beforeEach(() => {
      vi.useFakeTimers()
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    it('tracks last type cycle time per issue', async () => {
      const onCycleIssueType = vi.fn().mockResolvedValue(undefined)
      const { result } = renderHook(() =>
        useKeyboardNavigation({ projectId: mockProjectId, onCycleIssueType })
      )

      act(() => {
        result.current.initialize(mockRenderLines)
      })

      await act(async () => {
        await result.current.cycleIssueTypeAsync()
      })

      expect(onCycleIssueType).toHaveBeenCalledTimes(1)

      // Second call within debounce period should be ignored
      await act(async () => {
        await result.current.cycleIssueTypeAsync()
      })

      expect(onCycleIssueType).toHaveBeenCalledTimes(1)

      // After debounce period, should work again
      vi.advanceTimersByTime(3001)

      await act(async () => {
        await result.current.cycleIssueTypeAsync()
      })

      expect(onCycleIssueType).toHaveBeenCalledTimes(2)
    })
  })
})
