import { describe, it, expect, beforeEach, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useIssueSelection } from './use-issue-selection'
import type { TaskGraphRenderLine } from '../types'
import { IssueType } from '@/api'

describe('useIssueSelection', () => {
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

  describe('initialization', () => {
    it('initializes with no selection (-1 index)', () => {
      const { result } = renderHook(() => useIssueSelection())

      expect(result.current.selectedIndex).toBe(-1)
      expect(result.current.selectedIssueId).toBeUndefined()
    })

    it('allows setting render lines', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      // Still no selection after setting lines
      expect(result.current.selectedIndex).toBe(-1)
    })
  })

  describe('selectFirstActionable', () => {
    it('selects the first actionable issue', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectFirstActionable()
      })

      expect(result.current.selectedIndex).toBe(0) // First actionable is index 0
      expect(result.current.selectedIssueId).toBe('issue-1')
    })

    it('selects first actionable when lines passed directly', () => {
      const { result } = renderHook(() => useIssueSelection())

      // Can pass lines directly to avoid state batching issues
      act(() => {
        result.current.setRenderLines(mockRenderLines)
        result.current.selectFirstActionable(mockRenderLines)
      })

      expect(result.current.selectedIndex).toBe(0) // First actionable is index 0
      expect(result.current.selectedIssueId).toBe('issue-1')
    })

    it('falls back to first issue if none are actionable', () => {
      const noActionable: TaskGraphRenderLine[] = [
        {
          issueId: 'issue-1',
          title: 'First',
          lane: 0,
          issueType: IssueType.TASK,
          isActionable: false,
        },
        {
          issueId: 'issue-2',
          title: 'Second',
          lane: 1,
          issueType: IssueType.TASK,
          isActionable: false,
        },
      ]
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(noActionable)
      })

      act(() => {
        result.current.selectFirstActionable()
      })

      expect(result.current.selectedIndex).toBe(0)
      expect(result.current.selectedIssueId).toBe('issue-1')
    })

    it('does nothing with empty render lines', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines([])
      })

      act(() => {
        result.current.selectFirstActionable()
      })

      expect(result.current.selectedIndex).toBe(-1)
      expect(result.current.selectedIssueId).toBeUndefined()
    })
  })

  describe('selectIssue', () => {
    it('selects an issue by ID', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-3')
      })

      expect(result.current.selectedIndex).toBe(2)
      expect(result.current.selectedIssueId).toBe('issue-3')
    })

    it('does nothing if issue ID not found', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-1')
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.selectIssue('nonexistent')
      })

      // Selection unchanged
      expect(result.current.selectedIndex).toBe(0)
      expect(result.current.selectedIssueId).toBe('issue-1')
    })
  })

  describe('clearSelection', () => {
    it('clears the current selection', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-2')
      })

      expect(result.current.selectedIndex).toBe(1)

      act(() => {
        result.current.clearSelection()
      })

      expect(result.current.selectedIndex).toBe(-1)
      expect(result.current.selectedIssueId).toBeUndefined()
    })
  })

  describe('navigation: moveUp/moveDown', () => {
    beforeEach(() => {
      vi.clearAllMocks()
    })

    it('moveDown increases selection index', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-1')
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveDown()
      })

      expect(result.current.selectedIndex).toBe(1)
      expect(result.current.selectedIssueId).toBe('issue-2')
    })

    it('moveUp decreases selection index', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-2')
      })

      expect(result.current.selectedIndex).toBe(1)

      act(() => {
        result.current.moveUp()
      })

      expect(result.current.selectedIndex).toBe(0)
      expect(result.current.selectedIssueId).toBe('issue-1')
    })

    it('moveDown does nothing at the end of the list', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-4')
      })

      expect(result.current.selectedIndex).toBe(3)

      act(() => {
        result.current.moveDown()
      })

      expect(result.current.selectedIndex).toBe(3) // Unchanged
    })

    it('moveUp does nothing at the start of the list', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-1')
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveUp()
      })

      expect(result.current.selectedIndex).toBe(0) // Unchanged
    })

    it('moveDown does nothing with no selection', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      expect(result.current.selectedIndex).toBe(-1)

      act(() => {
        result.current.moveDown()
      })

      expect(result.current.selectedIndex).toBe(-1)
    })

    it('moveUp does nothing with no selection', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      expect(result.current.selectedIndex).toBe(-1)

      act(() => {
        result.current.moveUp()
      })

      expect(result.current.selectedIndex).toBe(-1)
    })
  })

  describe('navigation: moveToFirst/moveToLast', () => {
    it('moveToFirst selects the first issue', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-4')
      })

      expect(result.current.selectedIndex).toBe(3)

      act(() => {
        result.current.moveToFirst()
      })

      expect(result.current.selectedIndex).toBe(0)
      expect(result.current.selectedIssueId).toBe('issue-1')
    })

    it('moveToLast selects the last issue', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-1')
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveToLast()
      })

      expect(result.current.selectedIndex).toBe(3)
      expect(result.current.selectedIssueId).toBe('issue-4')
    })

    it('moveToFirst does nothing with empty list', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines([])
        result.current.moveToFirst()
      })

      expect(result.current.selectedIndex).toBe(-1)
    })

    it('moveToLast does nothing with empty list', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines([])
        result.current.moveToLast()
      })

      expect(result.current.selectedIndex).toBe(-1)
    })
  })

  describe('navigation: moveToParent/moveToChild', () => {
    it('moveToParent navigates to the parent issue', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-2') // Has parentLane: 0
      })

      expect(result.current.selectedIndex).toBe(1)

      act(() => {
        result.current.moveToParent()
      })

      // Should move to issue-1 which is at lane 0
      expect(result.current.selectedIssueId).toBe('issue-1')
    })

    it('moveToParent does nothing if no parent', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-1') // No parentLane
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveToParent()
      })

      expect(result.current.selectedIndex).toBe(0) // Unchanged
    })

    it('moveToChild navigates to the first child issue', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-1') // Lane 0, issue-2 has parentLane: 0
      })

      expect(result.current.selectedIndex).toBe(0)

      act(() => {
        result.current.moveToChild()
      })

      // Should move to issue-2 which has parentLane: 0
      expect(result.current.selectedIssueId).toBe('issue-2')
    })

    it('moveToChild does nothing if no children', () => {
      // For this test, we need an issue with no children
      const linesWithNoChildren: TaskGraphRenderLine[] = [
        {
          issueId: 'issue-1',
          title: 'First',
          lane: 0,
          issueType: IssueType.TASK,
          isActionable: true,
        },
        {
          issueId: 'issue-2',
          title: 'Second',
          lane: 1,
          issueType: IssueType.TASK,
          isActionable: false,
        },
      ]

      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(linesWithNoChildren)
      })

      act(() => {
        result.current.selectIssue('issue-2') // Lane 1, no children have parentLane: 1
      })

      expect(result.current.selectedIndex).toBe(1)

      act(() => {
        result.current.moveToChild()
      })

      expect(result.current.selectedIndex).toBe(1) // Unchanged
    })
  })

  describe('getSelectedRenderLine', () => {
    it('returns the selected render line', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-2')
      })

      expect(result.current.getSelectedRenderLine()).toEqual(mockRenderLines[1])
    })

    it('returns undefined when no selection', () => {
      const { result } = renderHook(() => useIssueSelection())

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      expect(result.current.getSelectedRenderLine()).toBeUndefined()
    })
  })

  describe('onSelectionChange callback', () => {
    it('calls onSelectionChange when selection changes', () => {
      const onSelectionChange = vi.fn()
      const { result } = renderHook(() => useIssueSelection({ onSelectionChange }))

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-1')
      })

      expect(onSelectionChange).toHaveBeenCalledWith('issue-1', 0)
    })

    it('calls onSelectionChange with undefined when cleared', () => {
      const onSelectionChange = vi.fn()
      const { result } = renderHook(() => useIssueSelection({ onSelectionChange }))

      act(() => {
        result.current.setRenderLines(mockRenderLines)
      })

      act(() => {
        result.current.selectIssue('issue-1')
      })

      onSelectionChange.mockClear()

      act(() => {
        result.current.clearSelection()
      })

      expect(onSelectionChange).toHaveBeenCalledWith(undefined, -1)
    })
  })
})
