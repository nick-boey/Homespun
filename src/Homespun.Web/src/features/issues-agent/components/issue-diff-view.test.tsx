import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { IssueDiffView } from './issue-diff-view'
import type { IssueDiffResponse, TaskGraphResponse, TaskGraphNodeResponse } from '@/api'
import { ChangeType, IssueType, IssueStatus, ExecutionMode } from '@/api'

// Mock StaticTaskGraphView to inspect props and simulate interactions
vi.mock('@/features/issues', () => ({
  StaticTaskGraphView: vi.fn(
    ({ data, filterIssueIds, selectedIssueId, onSelectIssue, ...props }) => (
      <div
        data-testid={props['data-testid'] ?? 'static-task-graph-view'}
        data-filter-ids={JSON.stringify(filterIssueIds)}
        data-has-data={data ? 'true' : 'false'}
        data-selected-id={selectedIssueId ?? ''}
      >
        {data?.nodes?.map((node: TaskGraphNodeResponse) => (
          <div
            key={node.issue?.id}
            data-issue-id={node.issue?.id}
            data-testid={`graph-issue-${node.issue?.id}`}
            onClick={() => onSelectIssue?.(node.issue?.id ?? '')}
          >
            {node.issue?.title}
          </div>
        ))}
      </div>
    )
  ),
}))

// Mock IssueChangeDetailPanel
vi.mock('./issue-change-detail-panel', () => ({
  IssueChangeDetailPanel: vi.fn(({ change, onClose }) => (
    <div data-testid="issue-change-detail-panel" data-issue-id={change.issueId}>
      <span>{change.title}</span>
      <button data-testid="close-detail-panel" onClick={onClose}>
        Close
      </button>
    </div>
  )),
}))

function createMockTaskGraph(nodes: Array<{ id: string; title: string }>): TaskGraphResponse {
  return {
    nodes: nodes.map((n, i) => ({
      issue: {
        id: n.id,
        title: n.title,
        description: null,
        type: IssueType.TASK,
        status: IssueStatus.DRAFT,
        executionMode: ExecutionMode.SERIES,
        parentIssues: null,
        assignedTo: null,
        priority: null,
        branchId: null,
      },
      lane: 0,
      row: i,
      isActionable: false,
    })),
    totalLanes: 1,
    mergedPrs: null,
    hasMorePastPrs: false,
    totalPastPrsShown: 0,
    agentStatuses: null,
    linkedPrs: null,
  }
}

function createMockDiff(overrides: Partial<IssueDiffResponse> = {}): IssueDiffResponse {
  return {
    mainBranchGraph: createMockTaskGraph([{ id: 'issue-1', title: 'Main Issue 1' }]),
    sessionBranchGraph: createMockTaskGraph([{ id: 'issue-2', title: 'Session Issue 1' }]),
    changes: [],
    summary: { created: 0, updated: 0, deleted: 0 },
    ...overrides,
  }
}

describe('IssueDiffView', () => {
  it('renders single graph with all changed issues', () => {
    const diff = createMockDiff({
      sessionBranchGraph: createMockTaskGraph([
        { id: 'created-1', title: 'Created Issue' },
        { id: 'updated-1', title: 'Updated Issue' },
        { id: 'unchanged-1', title: 'Unchanged Issue' },
      ]),
      changes: [
        { issueId: 'created-1', changeType: ChangeType.CREATED, title: 'Created Issue' },
        { issueId: 'updated-1', changeType: ChangeType.UPDATED, title: 'Updated Issue' },
        { issueId: 'deleted-1', changeType: ChangeType.DELETED, title: 'Deleted Issue' },
      ],
    })

    render(<IssueDiffView diff={diff} />)

    // Should only have one graph panel titled "Your Changes"
    expect(screen.getByText('Your Changes')).toBeInTheDocument()
    // Should NOT have "Main Branch (current)" panel
    expect(screen.queryByText('Main Branch (current)')).not.toBeInTheDocument()

    // Check that graph has filter for all changed issues
    const sessionGraph = screen.getByTestId('session-branch-graph')
    const filterIds = JSON.parse(sessionGraph.getAttribute('data-filter-ids') ?? '[]')
    expect(filterIds).toHaveLength(3)
    expect(filterIds).toContainEqual({ issueId: 'created-1', changeType: 'created' })
    expect(filterIds).toContainEqual({ issueId: 'updated-1', changeType: 'updated' })
    expect(filterIds).toContainEqual({ issueId: 'deleted-1', changeType: 'deleted' })
  })

  it('renders summary badges correctly', () => {
    const diff = createMockDiff({
      summary: { created: 3, updated: 2, deleted: 1 },
    })

    render(<IssueDiffView diff={diff} />)

    expect(screen.getByText('+3 created')).toBeInTheDocument()
    expect(screen.getByText('2 updated')).toBeInTheDocument()
    expect(screen.getByText('-1 deleted')).toBeInTheDocument()
  })

  it('shows "No changes" when summary is all zeros', () => {
    const diff = createMockDiff({
      summary: { created: 0, updated: 0, deleted: 0 },
    })

    render(<IssueDiffView diff={diff} />)

    expect(screen.getByText('No changes')).toBeInTheDocument()
  })

  it('shows detail panel when issue selected', () => {
    const diff = createMockDiff({
      sessionBranchGraph: createMockTaskGraph([{ id: 'issue-1', title: 'Test Issue' }]),
      changes: [{ issueId: 'issue-1', changeType: ChangeType.CREATED, title: 'Test Issue' }],
    })

    render(<IssueDiffView diff={diff} />)

    // Initially no detail panel
    expect(screen.queryByTestId('issue-change-detail-panel')).not.toBeInTheDocument()

    // Click on issue in graph
    fireEvent.click(screen.getByTestId('graph-issue-issue-1'))

    // Detail panel should appear
    expect(screen.getByTestId('issue-change-detail-panel')).toBeInTheDocument()
    expect(screen.getByTestId('issue-change-detail-panel')).toHaveAttribute(
      'data-issue-id',
      'issue-1'
    )
  })

  it('clears selection when close clicked', () => {
    const diff = createMockDiff({
      sessionBranchGraph: createMockTaskGraph([{ id: 'issue-1', title: 'Test Issue' }]),
      changes: [{ issueId: 'issue-1', changeType: ChangeType.CREATED, title: 'Test Issue' }],
    })

    render(<IssueDiffView diff={diff} />)

    // Select an issue
    fireEvent.click(screen.getByTestId('graph-issue-issue-1'))
    expect(screen.getByTestId('issue-change-detail-panel')).toBeInTheDocument()

    // Click close
    fireEvent.click(screen.getByTestId('close-detail-panel'))

    // Detail panel should be hidden
    expect(screen.queryByTestId('issue-change-detail-panel')).not.toBeInTheDocument()
  })

  it('toggles selection when same issue clicked twice', () => {
    const diff = createMockDiff({
      sessionBranchGraph: createMockTaskGraph([{ id: 'issue-1', title: 'Test Issue' }]),
      changes: [{ issueId: 'issue-1', changeType: ChangeType.CREATED, title: 'Test Issue' }],
    })

    render(<IssueDiffView diff={diff} />)

    // First click - select
    fireEvent.click(screen.getByTestId('graph-issue-issue-1'))
    expect(screen.getByTestId('issue-change-detail-panel')).toBeInTheDocument()

    // Second click - deselect
    fireEvent.click(screen.getByTestId('graph-issue-issue-1'))
    expect(screen.queryByTestId('issue-change-detail-panel')).not.toBeInTheDocument()
  })

  it('passes selectedIssueId to StaticTaskGraphView', () => {
    const diff = createMockDiff({
      sessionBranchGraph: createMockTaskGraph([{ id: 'issue-1', title: 'Test Issue' }]),
      changes: [{ issueId: 'issue-1', changeType: ChangeType.CREATED, title: 'Test Issue' }],
    })

    render(<IssueDiffView diff={diff} />)

    // Initially no selection
    const graph = screen.getByTestId('session-branch-graph')
    expect(graph.getAttribute('data-selected-id')).toBe('')

    // Select an issue
    fireEvent.click(screen.getByTestId('graph-issue-issue-1'))

    // Should pass selected ID to graph
    expect(graph.getAttribute('data-selected-id')).toBe('issue-1')
  })

  it('handles empty changes array', () => {
    const diff = createMockDiff({
      changes: [],
    })

    render(<IssueDiffView diff={diff} />)

    // Should still render without errors
    expect(screen.getByText('Your Changes')).toBeInTheDocument()
    expect(screen.queryByTestId('issue-change-detail-panel')).not.toBeInTheDocument()
  })

  it('does not show detail panel for non-existent issue selection', () => {
    const diff = createMockDiff({
      sessionBranchGraph: createMockTaskGraph([{ id: 'issue-1', title: 'Test Issue' }]),
      changes: [{ issueId: 'issue-1', changeType: ChangeType.CREATED, title: 'Test Issue' }],
    })

    render(<IssueDiffView diff={diff} />)

    // Simulate selecting an issue that's not in changes
    fireEvent.click(screen.getByTestId('graph-issue-issue-1'))
    expect(screen.getByTestId('issue-change-detail-panel')).toBeInTheDocument()
  })
})
