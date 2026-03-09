import { describe, it, expect, beforeEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useBranchIdGenerationStore } from './branch-id-generation-store'

describe('useBranchIdGenerationStore', () => {
  beforeEach(() => {
    // Reset store state before each test
    useBranchIdGenerationStore.setState({ generatingIssues: new Set() })
  })

  describe('markGenerating', () => {
    it('adds issue ID to generating set', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      act(() => {
        result.current.markGenerating('issue123')
      })

      expect(result.current.isGenerating('issue123')).toBe(true)
    })

    it('handles multiple issues generating simultaneously', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      act(() => {
        result.current.markGenerating('issue123')
        result.current.markGenerating('issue456')
        result.current.markGenerating('issue789')
      })

      expect(result.current.isGenerating('issue123')).toBe(true)
      expect(result.current.isGenerating('issue456')).toBe(true)
      expect(result.current.isGenerating('issue789')).toBe(true)
    })

    it('does not duplicate issue IDs', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      act(() => {
        result.current.markGenerating('issue123')
        result.current.markGenerating('issue123') // Duplicate
      })

      const state = useBranchIdGenerationStore.getState()
      expect(state.generatingIssues.size).toBe(1)
    })
  })

  describe('markComplete', () => {
    it('removes issue ID from generating set', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      act(() => {
        result.current.markGenerating('issue123')
        result.current.markComplete('issue123')
      })

      expect(result.current.isGenerating('issue123')).toBe(false)
    })

    it('handles removing non-existent issue gracefully', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      act(() => {
        result.current.markComplete('non-existent')
      })

      expect(result.current.isGenerating('non-existent')).toBe(false)
    })

    it('only removes specified issue', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      act(() => {
        result.current.markGenerating('issue123')
        result.current.markGenerating('issue456')
        result.current.markComplete('issue123')
      })

      expect(result.current.isGenerating('issue123')).toBe(false)
      expect(result.current.isGenerating('issue456')).toBe(true)
    })
  })

  describe('isGenerating', () => {
    it('returns false for non-generating issues', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      expect(result.current.isGenerating('issue123')).toBe(false)
    })

    it('returns true for generating issues', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      act(() => {
        result.current.markGenerating('issue123')
      })

      expect(result.current.isGenerating('issue123')).toBe(true)
    })
  })

  describe('store immutability', () => {
    it('creates new Set instance on markGenerating', () => {
      const initialState = useBranchIdGenerationStore.getState()
      const initialSet = initialState.generatingIssues

      act(() => {
        initialState.markGenerating('issue123')
      })

      const newState = useBranchIdGenerationStore.getState()
      expect(newState.generatingIssues).not.toBe(initialSet)
    })

    it('creates new Set instance on markComplete', () => {
      const { result } = renderHook(() => useBranchIdGenerationStore())

      act(() => {
        result.current.markGenerating('issue123')
      })

      const stateBeforeComplete = useBranchIdGenerationStore.getState()
      const setBeforeComplete = stateBeforeComplete.generatingIssues

      act(() => {
        result.current.markComplete('issue123')
      })

      const stateAfterComplete = useBranchIdGenerationStore.getState()
      expect(stateAfterComplete.generatingIssues).not.toBe(setBeforeComplete)
    })
  })
})
