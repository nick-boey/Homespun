import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { IssueDiffView } from './issue-diff-view'
import type { IssueDiffResponse, TaskGraphResponse, TaskGraphNodeResponse } from '@/api'
import { ChangeType, IssueType, IssueStatus, ExecutionMode } from '@/api'

// Mock StaticTaskGraphView to inspect props
vi.mock('@/features/issues', () => ({
  StaticTaskGraphView: vi.fn(({ data, filterIssueIds, ...props }) => (
    <div
      data-testid={props['data-testid'] ?? 'static-task-graph-view'}
      data-filter-ids={JSON.stringify(filterIssueIds)}
      data-has-data={data ? 'true' : 'false'}
    >
      {data?.nodes?.map((node: TaskGraphNodeResponse) => (
        <div key={node.issue?.id} data-issue-id={node.issue?.id}>
          {node.issue?.title}
        </div>
      ))}
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
  it('renders main branch graph with mainBranchGraph data', () => {
    const diff = createMockDiff({
      mainBranchGraph: createMockTaskGraph([
        { id: 'main-1', title: 'Main Issue One' },
        { id: 'main-2', title: 'Main Issue Two' },
      ]),
    })

    render(<IssueDiffView diff={diff} />)

    // Main branch panel should be present
    expect(screen.getByText('Main Branch (current)')).toBeInTheDocument()
    // Main branch should show main branch issues
    expect(screen.getByText('Main Issue One')).toBeInTheDocument()
    expect(screen.getByText('Main Issue Two')).toBeInTheDocument()
  })

  it('renders session branch graph with sessionBranchGraph data', () => {
    const diff = createMockDiff({
      sessionBranchGraph: createMockTaskGraph([
        { id: 'session-1', title: 'Session Issue One' },
        { id: 'session-2', title: 'Session Issue Two' },
      ]),
    })

    render(<IssueDiffView diff={diff} />)

    // Session branch panel should be present
    expect(screen.getByText('Your Changes')).toBeInTheDocument()
    // Session branch should show session issues
    expect(screen.getByText('Session Issue One')).toBeInTheDocument()
    expect(screen.getByText('Session Issue Two')).toBeInTheDocument()
  })

  it('filters main branch graph to show only deleted issues', () => {
    const diff = createMockDiff({
      mainBranchGraph: createMockTaskGraph([
        { id: 'existing-1', title: 'Existing Issue' },
        { id: 'deleted-1', title: 'Deleted Issue' },
      ]),
      changes: [{ issueId: 'deleted-1', changeType: ChangeType.DELETED, title: 'Deleted Issue' }],
    })

    render(<IssueDiffView diff={diff} />)

    // Check that main branch graph has filter for deleted issues
    const mainGraph = screen.getByTestId('main-branch-graph')
    const filterIds = JSON.parse(mainGraph.getAttribute('data-filter-ids') ?? '[]')
    expect(filterIds).toEqual([{ issueId: 'deleted-1', changeType: 'deleted' }])
  })

  it('filters session branch graph to show only created/updated issues', () => {
    const diff = createMockDiff({
      sessionBranchGraph: createMockTaskGraph([
        { id: 'created-1', title: 'Created Issue' },
        { id: 'updated-1', title: 'Updated Issue' },
        { id: 'unchanged-1', title: 'Unchanged Issue' },
      ]),
      changes: [
        { issueId: 'created-1', changeType: ChangeType.CREATED, title: 'Created Issue' },
        { issueId: 'updated-1', changeType: ChangeType.UPDATED, title: 'Updated Issue' },
      ],
    })

    render(<IssueDiffView diff={diff} />)

    // Check that session branch graph has filter for created/updated issues
    const sessionGraph = screen.getByTestId('session-branch-graph')
    const filterIds = JSON.parse(sessionGraph.getAttribute('data-filter-ids') ?? '[]')
    expect(filterIds).toHaveLength(2)
    expect(filterIds).toContainEqual({ issueId: 'created-1', changeType: 'created' })
    expect(filterIds).toContainEqual({ issueId: 'updated-1', changeType: 'updated' })
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

  it('renders change details section when changes exist', () => {
    const diff = createMockDiff({
      changes: [
        { issueId: 'issue-1', changeType: ChangeType.CREATED, title: 'New Issue' },
        { issueId: 'issue-2', changeType: ChangeType.UPDATED, title: 'Modified Issue' },
      ],
    })

    render(<IssueDiffView diff={diff} />)

    expect(screen.getByText('Change Details')).toBeInTheDocument()
    expect(screen.getByText('New Issue')).toBeInTheDocument()
    expect(screen.getByText('Modified Issue')).toBeInTheDocument()
  })

  it('does not render change details section when no changes', () => {
    const diff = createMockDiff({ changes: [] })

    render(<IssueDiffView diff={diff} />)

    expect(screen.queryByText('Change Details')).not.toBeInTheDocument()
  })
})
