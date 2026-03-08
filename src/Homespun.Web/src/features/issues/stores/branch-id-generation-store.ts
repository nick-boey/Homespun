import { create } from 'zustand'

interface BranchIdGenerationState {
  /** Map of issue IDs that are currently generating branch IDs */
  generatingIssues: Set<string>
  /** Add an issue to the generating set */
  markGenerating: (issueId: string) => void
  /** Remove an issue from the generating set */
  markComplete: (issueId: string) => void
  /** Check if an issue is currently generating */
  isGenerating: (issueId: string) => boolean
}

/**
 * Store to track branch ID generation state across the application.
 * Used to show loading indicators while branch IDs are being generated.
 */
export const useBranchIdGenerationStore = create<BranchIdGenerationState>((set, get) => ({
  generatingIssues: new Set(),

  markGenerating: (issueId: string) => {
    set((state) => ({
      generatingIssues: new Set(state.generatingIssues).add(issueId),
    }))
  },

  markComplete: (issueId: string) => {
    set((state) => {
      const newSet = new Set(state.generatingIssues)
      newSet.delete(issueId)
      return { generatingIssues: newSet }
    })
  },

  isGenerating: (issueId: string) => {
    return get().generatingIssues.has(issueId)
  },
}))
