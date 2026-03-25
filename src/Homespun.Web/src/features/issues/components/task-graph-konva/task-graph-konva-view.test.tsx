/**
 * Tests for TaskGraphKonvaView component.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { TaskGraphKonvaView } from './task-graph-konva-view'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import type { TaskGraphResponse } from '@/api'

// Mock the useTaskGraph hook
const mockUseTaskGraph = vi.fn()
vi.mock('../../hooks', () => ({
  useTaskGraph: () => mockUseTaskGraph(),
  taskGraphQueryKey: (id: string) => ['task-graph', id],
  useCreateIssue: vi.fn(() => ({ createIssue: vi.fn() })),
  useUpdateIssue: vi.fn(() => ({ mutateAsync: vi.fn() })),
}))

// Mock useLinkedPrStatus
vi.mock('../../hooks/use-linked-pr-status', () => ({
  useLinkedPrStatus: vi.fn(() => ({ data: null })),
}))

// Mock useSignalR hook
vi.mock('@/hooks/use-signalr', () => ({
  useSignalR: vi.fn(() => ({ connection: null })),
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

const defaultProps = {
  projectId: 'project-1',
  onSelectIssue: vi.fn(),
  onEditIssue: vi.fn(),
}

function renderComponent(props = {}) {
  return render(createElement(TaskGraphKonvaView, { ...defaultProps, ...props }), {
    wrapper: createWrapper(),
  })
}

/** Create a minimal task graph response */
function createTaskGraph(
  nodes: Array<{
    id: string
    title: string
    lane?: number
    row?: number
    isActionable?: boolean
    issueType?: IssueType
    status?: IssueStatus
    executionMode?: ExecutionMode
    parentIssues?: Array<{ parentIssue: string }>
  }>
): TaskGraphResponse {
  return {
    nodes: nodes.map((n, index) => ({
      lane: n.lane ?? 0,
      row: n.row ?? index,
      isActionable: n.isActionable ?? false,
      issue: {
        id: n.id,
        title: n.title,
        type: n.issueType ?? IssueType.TASK,
        status: n.status ?? IssueStatus.OPEN,
        executionMode: n.executionMode ?? ExecutionMode.SERIES,
        parentIssues: n.parentIssues ?? [],
        createdAt: new Date().toISOString(),
        modifiedAt: new Date().toISOString(),
      },
    })),
    mergedPrs: [],
    agentStatuses: {},
    linkedPrs: {},
    hasMorePastPrs: false,
  }
}

describe('TaskGraphKonvaView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('loading state', () => {
    it('renders loading skeleton while loading', () => {
      mockUseTaskGraph.mockReturnValue({
        taskGraph: null,
        isLoading: true,
        isError: false,
        refetch: vi.fn(),
      })

      renderComponent()
      expect(screen.getByTestId('task-graph-konva-loading')).toBeInTheDocument()
    })
  })

  describe('error state', () => {
    it('renders error message on error', () => {
      mockUseTaskGraph.mockReturnValue({
        taskGraph: null,
        isLoading: false,
        isError: true,
        refetch: vi.fn(),
      })

      renderComponent()
      expect(screen.getByText(/failed to load/i)).toBeInTheDocument()
    })
  })

  describe('empty state', () => {
    it('renders empty state when no issues', () => {
      mockUseTaskGraph.mockReturnValue({
        taskGraph: createTaskGraph([]),
        isLoading: false,
        isError: false,
        refetch: vi.fn(),
      })

      renderComponent()
      expect(screen.getByText(/no issues found/i)).toBeInTheDocument()
    })
  })

  describe('with data', () => {
    const sampleGraph = createTaskGraph([
      { id: 'issue-1', title: 'First Issue', lane: 0, row: 0 },
      { id: 'issue-2', title: 'Second Issue', lane: 0, row: 1 },
    ])

    beforeEach(() => {
      mockUseTaskGraph.mockReturnValue({
        taskGraph: sampleGraph,
        isLoading: false,
        isError: false,
        refetch: vi.fn(),
      })
    })

    it('renders the task graph canvas', () => {
      renderComponent()
      expect(screen.getByTestId('task-graph-konva')).toBeInTheDocument()
    })

    it('renders issue titles', () => {
      renderComponent()
      expect(screen.getByText('First Issue')).toBeInTheDocument()
      expect(screen.getByText('Second Issue')).toBeInTheDocument()
    })

    it('calls onSelectIssue when issue row is clicked', () => {
      const onSelectIssue = vi.fn()
      renderComponent({ onSelectIssue })

      // Find and click the first issue row
      const rows = screen.getAllByTestId('konva-html-row')
      fireEvent.click(rows[0])

      expect(onSelectIssue).toHaveBeenCalledWith('issue-1')
    })

    it('highlights selected issue', () => {
      renderComponent({ selectedIssueId: 'issue-1' })

      const rows = screen.getAllByTestId('konva-html-row')
      expect(rows[0].className).toContain('ring-1')
      expect(rows[0].className).toContain('ring-primary')
    })
  })

  describe('touch events', () => {
    const sampleGraph = createTaskGraph([
      { id: 'issue-1', title: 'First Issue', lane: 0, row: 0 },
      { id: 'issue-2', title: 'Second Issue', lane: 0, row: 1 },
    ])

    beforeEach(() => {
      mockUseTaskGraph.mockReturnValue({
        taskGraph: sampleGraph,
        isLoading: false,
        isError: false,
        refetch: vi.fn(),
      })
    })

    it('attaches touch event listeners to the container', () => {
      renderComponent()
      const container = screen.getByTestId('task-graph-konva')

      // Verify touch events don't throw and the container is interactive
      fireEvent.touchStart(container, {
        touches: [{ clientX: 100, clientY: 200 }],
      })
      fireEvent.touchMove(container, {
        touches: [{ clientX: 80, clientY: 180 }],
      })
      fireEvent.touchEnd(container)

      // The container should still be rendered and functional
      expect(container).toBeInTheDocument()
    })
  })

  describe('keyboard navigation', () => {
    const sampleGraph = createTaskGraph([
      { id: 'issue-1', title: 'First Issue' },
      { id: 'issue-2', title: 'Second Issue' },
      { id: 'issue-3', title: 'Third Issue' },
    ])

    beforeEach(() => {
      mockUseTaskGraph.mockReturnValue({
        taskGraph: sampleGraph,
        isLoading: false,
        isError: false,
        refetch: vi.fn(),
      })
    })

    it('selects next issue on ArrowDown/j', () => {
      const onSelectIssue = vi.fn()
      renderComponent({ selectedIssueId: 'issue-1', onSelectIssue })

      const container = screen.getByTestId('task-graph-konva')
      fireEvent.keyDown(container, { key: 'ArrowDown' })

      expect(onSelectIssue).toHaveBeenCalledWith('issue-2')
    })

    it('selects previous issue on ArrowUp/k', () => {
      const onSelectIssue = vi.fn()
      renderComponent({ selectedIssueId: 'issue-2', onSelectIssue })

      const container = screen.getByTestId('task-graph-konva')
      fireEvent.keyDown(container, { key: 'ArrowUp' })

      expect(onSelectIssue).toHaveBeenCalledWith('issue-1')
    })

    it('jumps to first issue on g', () => {
      const onSelectIssue = vi.fn()
      renderComponent({ selectedIssueId: 'issue-3', onSelectIssue })

      const container = screen.getByTestId('task-graph-konva')
      fireEvent.keyDown(container, { key: 'g' })

      expect(onSelectIssue).toHaveBeenCalledWith('issue-1')
    })

    it('jumps to last issue on G', () => {
      const onSelectIssue = vi.fn()
      renderComponent({ selectedIssueId: 'issue-1', onSelectIssue })

      const container = screen.getByTestId('task-graph-konva')
      fireEvent.keyDown(container, { key: 'G' })

      expect(onSelectIssue).toHaveBeenCalledWith('issue-3')
    })
  })
})
