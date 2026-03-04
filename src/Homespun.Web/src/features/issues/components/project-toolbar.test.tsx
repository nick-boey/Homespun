import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import React from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProjectToolbar } from './project-toolbar'

// Mock the useIssueHistory hook
vi.mock('../hooks/use-issue-history', () => ({
  useIssueHistory: vi.fn(() => ({
    canUndo: false,
    canRedo: false,
    undoDescription: null,
    redoDescription: null,
    undo: vi.fn(),
    redo: vi.fn(),
    isUndoing: false,
    isRedoing: false,
    isLoading: false,
  })),
}))

import { useIssueHistory } from '../hooks/use-issue-history'

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

const defaultProps = {
  projectId: 'test-project',
  selectedIssueId: null as string | null,
  onCreateAbove: vi.fn(),
  onCreateBelow: vi.fn(),
  onMakeChild: vi.fn(),
  onMakeParent: vi.fn(),
  onEditIssue: vi.fn(),
  onOpenAgentLauncher: vi.fn(),
  depth: 3,
  onDepthChange: vi.fn(),
  searchQuery: '',
  onSearchChange: vi.fn(),
  searchMatchCount: 0,
  onNextMatch: vi.fn(),
  onPreviousMatch: vi.fn(),
  onEmbedSearch: vi.fn(),
}

function renderToolbar(props = {}) {
  return render(React.createElement(ProjectToolbar, { ...defaultProps, ...props }), {
    wrapper: createWrapper(),
  })
}

describe('ProjectToolbar', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useIssueHistory).mockReturnValue({
      canUndo: false,
      canRedo: false,
      undoDescription: null,
      redoDescription: null,
      undo: vi.fn(),
      redo: vi.fn(),
      isUndoing: false,
      isRedoing: false,
      isLoading: false,
      historyState: undefined,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })
  })

  describe('Creation buttons', () => {
    it('renders create above button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /create above/i })).toBeInTheDocument()
    })

    it('renders create below button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /create below/i })).toBeInTheDocument()
    })

    it('calls onCreateAbove when create above button is clicked', async () => {
      const user = userEvent.setup()
      const onCreateAbove = vi.fn()
      renderToolbar({ onCreateAbove })

      await user.click(screen.getByRole('button', { name: /create above/i }))
      expect(onCreateAbove).toHaveBeenCalled()
    })

    it('calls onCreateBelow when create below button is clicked', async () => {
      const user = userEvent.setup()
      const onCreateBelow = vi.fn()
      renderToolbar({ onCreateBelow })

      await user.click(screen.getByRole('button', { name: /create below/i }))
      expect(onCreateBelow).toHaveBeenCalled()
    })
  })

  describe('Hierarchy buttons', () => {
    it('renders make child button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /make child/i })).toBeInTheDocument()
    })

    it('renders make parent button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /make parent/i })).toBeInTheDocument()
    })

    it('calls onMakeChild when make child button is clicked', async () => {
      const user = userEvent.setup()
      const onMakeChild = vi.fn()
      renderToolbar({ onMakeChild, selectedIssueId: 'test-issue-1' })

      await user.click(screen.getByRole('button', { name: /make child/i }))
      expect(onMakeChild).toHaveBeenCalled()
    })

    it('calls onMakeParent when make parent button is clicked', async () => {
      const user = userEvent.setup()
      const onMakeParent = vi.fn()
      renderToolbar({ onMakeParent, selectedIssueId: 'test-issue-1' })

      await user.click(screen.getByRole('button', { name: /make parent/i }))
      expect(onMakeParent).toHaveBeenCalled()
    })
  })

  describe('Undo/Redo buttons', () => {
    it('renders undo button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /undo/i })).toBeInTheDocument()
    })

    it('renders redo button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /redo/i })).toBeInTheDocument()
    })

    it('disables undo button when canUndo is false', () => {
      vi.mocked(useIssueHistory).mockReturnValue({
        canUndo: false,
        canRedo: false,
        undoDescription: null,
        redoDescription: null,
        undo: vi.fn(),
        redo: vi.fn(),
        isUndoing: false,
        isRedoing: false,
        isLoading: false,
        historyState: undefined,
        isError: false,
        error: null,
        refetch: vi.fn(),
      })
      renderToolbar()

      expect(screen.getByRole('button', { name: /undo/i })).toBeDisabled()
    })

    it('enables undo button when canUndo is true', () => {
      vi.mocked(useIssueHistory).mockReturnValue({
        canUndo: true,
        canRedo: false,
        undoDescription: 'Create issue',
        redoDescription: null,
        undo: vi.fn(),
        redo: vi.fn(),
        isUndoing: false,
        isRedoing: false,
        isLoading: false,
        historyState: undefined,
        isError: false,
        error: null,
        refetch: vi.fn(),
      })
      renderToolbar()

      expect(screen.getByRole('button', { name: /undo/i })).not.toBeDisabled()
    })

    it('disables redo button when canRedo is false', () => {
      vi.mocked(useIssueHistory).mockReturnValue({
        canUndo: false,
        canRedo: false,
        undoDescription: null,
        redoDescription: null,
        undo: vi.fn(),
        redo: vi.fn(),
        isUndoing: false,
        isRedoing: false,
        isLoading: false,
        historyState: undefined,
        isError: false,
        error: null,
        refetch: vi.fn(),
      })
      renderToolbar()

      expect(screen.getByRole('button', { name: /redo/i })).toBeDisabled()
    })

    it('enables redo button when canRedo is true', () => {
      vi.mocked(useIssueHistory).mockReturnValue({
        canUndo: false,
        canRedo: true,
        undoDescription: null,
        redoDescription: 'Create issue',
        undo: vi.fn(),
        redo: vi.fn(),
        isUndoing: false,
        isRedoing: false,
        isLoading: false,
        historyState: undefined,
        isError: false,
        error: null,
        refetch: vi.fn(),
      })
      renderToolbar()

      expect(screen.getByRole('button', { name: /redo/i })).not.toBeDisabled()
    })

    it('calls undo when undo button is clicked', async () => {
      const user = userEvent.setup()
      const undo = vi.fn()
      vi.mocked(useIssueHistory).mockReturnValue({
        canUndo: true,
        canRedo: false,
        undoDescription: 'Create issue',
        redoDescription: null,
        undo,
        redo: vi.fn(),
        isUndoing: false,
        isRedoing: false,
        isLoading: false,
        historyState: undefined,
        isError: false,
        error: null,
        refetch: vi.fn(),
      })
      renderToolbar()

      await user.click(screen.getByRole('button', { name: /undo/i }))
      expect(undo).toHaveBeenCalled()
    })

    it('calls redo when redo button is clicked', async () => {
      const user = userEvent.setup()
      const redo = vi.fn()
      vi.mocked(useIssueHistory).mockReturnValue({
        canUndo: false,
        canRedo: true,
        undoDescription: null,
        redoDescription: 'Create issue',
        undo: vi.fn(),
        redo,
        isUndoing: false,
        isRedoing: false,
        isLoading: false,
        historyState: undefined,
        isError: false,
        error: null,
        refetch: vi.fn(),
      })
      renderToolbar()

      await user.click(screen.getByRole('button', { name: /redo/i }))
      expect(redo).toHaveBeenCalled()
    })
  })

  describe('Edit button', () => {
    it('renders edit button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /edit issue/i })).toBeInTheDocument()
    })

    it('disables edit button when no issue is selected', () => {
      renderToolbar({ selectedIssueId: null })
      expect(screen.getByRole('button', { name: /edit issue/i })).toBeDisabled()
    })

    it('enables edit button when an issue is selected', () => {
      renderToolbar({ selectedIssueId: 'issue-123' })
      expect(screen.getByRole('button', { name: /edit issue/i })).not.toBeDisabled()
    })

    it('calls onEditIssue when edit button is clicked', async () => {
      const user = userEvent.setup()
      const onEditIssue = vi.fn()
      renderToolbar({ selectedIssueId: 'issue-123', onEditIssue })

      await user.click(screen.getByRole('button', { name: /edit issue/i }))
      expect(onEditIssue).toHaveBeenCalled()
    })
  })

  describe('Agent Run button', () => {
    it('renders agent run button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /run agent/i })).toBeInTheDocument()
    })

    it('calls onOpenAgentLauncher when agent run button is clicked', async () => {
      const user = userEvent.setup()
      const onOpenAgentLauncher = vi.fn()
      renderToolbar({ onOpenAgentLauncher })

      await user.click(screen.getByRole('button', { name: /run agent/i }))
      expect(onOpenAgentLauncher).toHaveBeenCalled()
    })
  })

  describe('Depth controls', () => {
    it('renders depth decrease button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /decrease depth/i })).toBeInTheDocument()
    })

    it('renders depth increase button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /increase depth/i })).toBeInTheDocument()
    })

    it('displays current depth value', () => {
      renderToolbar({ depth: 5 })
      expect(screen.getByText('5')).toBeInTheDocument()
    })

    it('calls onDepthChange with decreased value when decrease button is clicked', async () => {
      const user = userEvent.setup()
      const onDepthChange = vi.fn()
      renderToolbar({ depth: 3, onDepthChange })

      await user.click(screen.getByRole('button', { name: /decrease depth/i }))
      expect(onDepthChange).toHaveBeenCalledWith(2)
    })

    it('calls onDepthChange with increased value when increase button is clicked', async () => {
      const user = userEvent.setup()
      const onDepthChange = vi.fn()
      renderToolbar({ depth: 3, onDepthChange })

      await user.click(screen.getByRole('button', { name: /increase depth/i }))
      expect(onDepthChange).toHaveBeenCalledWith(4)
    })

    it('disables decrease button when depth is 1', () => {
      renderToolbar({ depth: 1 })
      expect(screen.getByRole('button', { name: /decrease depth/i })).toBeDisabled()
    })
  })

  describe('Search input', () => {
    it('renders search input', () => {
      renderToolbar()
      expect(screen.getByRole('searchbox')).toBeInTheDocument()
    })

    it('displays search query value', () => {
      renderToolbar({ searchQuery: 'test search' })
      expect(screen.getByRole('searchbox')).toHaveValue('test search')
    })

    it('calls onSearchChange when search input changes', async () => {
      const user = userEvent.setup()
      const onSearchChange = vi.fn()
      renderToolbar({ onSearchChange })

      await user.type(screen.getByRole('searchbox'), 'hello')
      expect(onSearchChange).toHaveBeenCalled()
    })

    it('displays match count when there are matches', () => {
      renderToolbar({ searchQuery: 'test', searchMatchCount: 5 })
      expect(screen.getByText('5')).toBeInTheDocument()
    })

    it('does not display match count when search is empty', () => {
      renderToolbar({ searchQuery: '', searchMatchCount: 0 })
      // Look for elements that might indicate a count badge
      const badges = screen.queryByTestId('search-match-count')
      expect(badges).toBeNull()
    })
  })

  describe('Toolbar layout', () => {
    it('has sticky positioning class', () => {
      renderToolbar()
      const toolbar = screen.getByRole('toolbar')
      expect(toolbar).toHaveClass('sticky')
    })

    it('has horizontal scroll class for mobile', () => {
      renderToolbar()
      const toolbar = screen.getByRole('toolbar')
      expect(toolbar).toHaveClass('overflow-x-auto')
    })
  })
})
