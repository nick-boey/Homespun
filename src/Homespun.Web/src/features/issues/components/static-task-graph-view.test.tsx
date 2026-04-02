import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { StaticTaskGraphView, type FilteredIssue } from './static-task-graph-view'
import type { TaskGraphResponse, TaskGraphNodeResponse } from '@/api'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'

// Mock computeLayout to control what gets rendered
vi.mock('../services', async (importOriginal) => {
  const original = await importOriginal<typeof import('../services')>()
  return {
    ...original,
    computeLayout: vi.fn((taskGraph) => {
      if (!taskGraph?.nodes?.length) return []
      return taskGraph.nodes.map((node: TaskGraphNodeResponse, index: number) => ({
        type: 'issue' as const,
        issueId: node.issue?.id ?? '',
        title: node.issue?.title ?? '',
        description: node.issue?.description ?? null,
        branchName: null,
        lane: node.lane ?? 0,
        marker: 'open',
        parentLane: null,
        isFirstChild: index === 0,
        isSeriesChild: false,
        drawTopLine: false,
        drawBottomLine: false,
        seriesConnectorFromLane: null,
        issueType: node.issue?.type ?? IssueType.TASK,
        status: node.issue?.status ?? IssueStatus.DRAFT,
        hasDescription: false,
        linkedPr: null,
        agentStatus: null,
        assignedTo: null,
        drawLane0Connector: false,
        isLastLane0Connector: false,
        drawLane0PassThrough: false,
        lane0Color: null,
        hasHiddenParent: false,
        hiddenParentIsSeriesMode: false,
        executionMode: ExecutionMode.SERIES,
      }))
    }),
  }
})

function createMockTaskGraph(nodes: Partial<TaskGraphNodeResponse>[]): TaskGraphResponse {
  return {
    nodes: nodes.map((n, i) => ({
      issue: {
        id: n.issue?.id ?? `issue-${i}`,
        title: n.issue?.title ?? `Issue ${i}`,
        description: n.issue?.description ?? null,
        type: n.issue?.type ?? IssueType.TASK,
        status: n.issue?.status ?? IssueStatus.DRAFT,
        executionMode: n.issue?.executionMode ?? ExecutionMode.SERIES,
        parentIssues: n.issue?.parentIssues ?? null,
        assignedTo: n.issue?.assignedTo ?? null,
        priority: n.issue?.priority ?? null,
        workingBranchId: n.issue?.workingBranchId ?? null,
      },
      lane: n.lane ?? 0,
      row: n.row ?? i,
      isActionable: n.isActionable ?? false,
    })),
    totalLanes: 1,
    mergedPrs: null,
    hasMorePastPrs: false,
    totalPastPrsShown: 0,
    agentStatuses: null,
    linkedPrs: null,
  }
}

describe('StaticTaskGraphView', () => {
  it('renders loading skeleton when data is undefined', () => {
    render(<StaticTaskGraphView data={undefined} />)

    expect(screen.getByTestId('static-task-graph-loading')).toBeInTheDocument()
    // Should render multiple skeletons
    const skeletons = screen.getAllByTestId('issue-row-skeleton')
    expect(skeletons.length).toBeGreaterThan(0)
  })

  it('renders empty state when data has no nodes', () => {
    const emptyData: TaskGraphResponse = {
      nodes: [],
      totalLanes: 0,
      mergedPrs: null,
      hasMorePastPrs: false,
      totalPastPrsShown: 0,
      agentStatuses: null,
      linkedPrs: null,
    }

    render(<StaticTaskGraphView data={emptyData} />)

    expect(screen.getByTestId('static-task-graph-empty')).toBeInTheDocument()
    expect(screen.getByText('No issues to display')).toBeInTheDocument()
  })

  it('renders issues from provided data', () => {
    const data = createMockTaskGraph([
      { issue: { id: 'issue-1', title: 'First Issue' } },
      { issue: { id: 'issue-2', title: 'Second Issue' } },
    ])

    render(<StaticTaskGraphView data={data} />)

    expect(screen.getByTestId('static-task-graph')).toBeInTheDocument()
    expect(screen.getByText('First Issue')).toBeInTheDocument()
    expect(screen.getByText('Second Issue')).toBeInTheDocument()
  })

  it('shows all issues but fades unchanged ones when filterIssueIds provided', () => {
    const data = createMockTaskGraph([
      { issue: { id: 'issue-1', title: 'First Issue' } },
      { issue: { id: 'issue-2', title: 'Second Issue' } },
      { issue: { id: 'issue-3', title: 'Third Issue' } },
    ])

    const filterIssueIds: FilteredIssue[] = [
      { issueId: 'issue-1', changeType: 'created' },
      { issueId: 'issue-3', changeType: 'updated' },
    ]

    render(<StaticTaskGraphView data={data} filterIssueIds={filterIssueIds} />)

    // All issues should be visible
    expect(screen.getByText('First Issue')).toBeInTheDocument()
    expect(screen.getByText('Second Issue')).toBeInTheDocument()
    expect(screen.getByText('Third Issue')).toBeInTheDocument()

    // Unchanged issue should be faded
    const rows = screen.getAllByTestId('static-task-graph-issue-row')
    expect(rows[1]).toHaveClass('opacity-50')
    // Changed issues should not be faded
    expect(rows[0]).not.toHaveClass('opacity-50')
    expect(rows[2]).not.toHaveClass('opacity-50')
  })

  it('applies created change type style (green)', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'New Issue' } }])

    const filterIssueIds: FilteredIssue[] = [{ issueId: 'issue-1', changeType: 'created' }]

    render(<StaticTaskGraphView data={data} filterIssueIds={filterIssueIds} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    expect(row).toHaveClass('border-green-500')
  })

  it('applies updated change type style (yellow)', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'Updated Issue' } }])

    const filterIssueIds: FilteredIssue[] = [{ issueId: 'issue-1', changeType: 'updated' }]

    render(<StaticTaskGraphView data={data} filterIssueIds={filterIssueIds} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    expect(row).toHaveClass('border-yellow-500')
  })

  it('applies deleted change type style (red with line-through)', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'Deleted Issue' } }])

    const filterIssueIds: FilteredIssue[] = [{ issueId: 'issue-1', changeType: 'deleted' }]

    render(<StaticTaskGraphView data={data} filterIssueIds={filterIssueIds} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    expect(row).toHaveClass('border-red-500')
    // Title should have line-through
    const title = screen.getByText('Deleted Issue')
    expect(title).toHaveClass('line-through')
  })

  it('applies custom className', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'Test Issue' } }])

    render(<StaticTaskGraphView data={data} className="custom-class" />)

    expect(screen.getByTestId('static-task-graph')).toHaveClass('custom-class')
  })

  it('renders without filterIssueIds showing all issues', () => {
    const data = createMockTaskGraph([
      { issue: { id: 'issue-1', title: 'First Issue' } },
      { issue: { id: 'issue-2', title: 'Second Issue' } },
    ])

    render(<StaticTaskGraphView data={data} />)

    expect(screen.getByText('First Issue')).toBeInTheDocument()
    expect(screen.getByText('Second Issue')).toBeInTheDocument()
  })

  it('shows all issues faded when filter matches none', () => {
    const data = createMockTaskGraph([
      { issue: { id: 'issue-1', title: 'First Issue' } },
      { issue: { id: 'issue-2', title: 'Second Issue' } },
    ])

    // Filter for an issue that doesn't exist — all issues render but faded
    const filterIssueIds: FilteredIssue[] = [{ issueId: 'non-existent', changeType: 'created' }]

    render(<StaticTaskGraphView data={data} filterIssueIds={filterIssueIds} />)

    expect(screen.getByText('First Issue')).toBeInTheDocument()
    expect(screen.getByText('Second Issue')).toBeInTheDocument()
    const rows = screen.getAllByTestId('static-task-graph-issue-row')
    expect(rows[0]).toHaveClass('opacity-50')
    expect(rows[1]).toHaveClass('opacity-50')
  })

  it('applies faded styling to unchanged issues when filterIssueIds provided', () => {
    const data = createMockTaskGraph([
      { issue: { id: 'issue-1', title: 'Changed Issue' } },
      { issue: { id: 'issue-2', title: 'Unchanged Issue A' } },
      { issue: { id: 'issue-3', title: 'Unchanged Issue B' } },
    ])

    const filterIssueIds: FilteredIssue[] = [{ issueId: 'issue-1', changeType: 'updated' }]

    render(<StaticTaskGraphView data={data} filterIssueIds={filterIssueIds} />)

    const rows = screen.getAllByTestId('static-task-graph-issue-row')
    // Changed issue should not be faded
    expect(rows[0]).not.toHaveClass('opacity-50')
    // Unchanged issues should be faded with italic text
    expect(rows[1]).toHaveClass('opacity-50')
    expect(rows[2]).toHaveClass('opacity-50')
    expect(screen.getByText('Unchanged Issue A')).toHaveClass('italic')
    expect(screen.getByText('Unchanged Issue B')).toHaveClass('italic')
  })

  it('does not apply faded styling when filterIssueIds not provided', () => {
    const data = createMockTaskGraph([
      { issue: { id: 'issue-1', title: 'First Issue' } },
      { issue: { id: 'issue-2', title: 'Second Issue' } },
    ])

    render(<StaticTaskGraphView data={data} />)

    const rows = screen.getAllByTestId('static-task-graph-issue-row')
    expect(rows[0]).not.toHaveClass('opacity-50')
    expect(rows[1]).not.toHaveClass('opacity-50')
  })

  it('is read-only and does not include interactive elements', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'Test Issue' } }])

    render(<StaticTaskGraphView data={data} />)

    // Should not have buttons for actions
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    // Container should not be focusable
    const container = screen.getByTestId('static-task-graph')
    expect(container).not.toHaveAttribute('tabindex')
  })

  it('highlights selected issue with ring styling', () => {
    const data = createMockTaskGraph([
      { issue: { id: 'issue-1', title: 'First Issue' } },
      { issue: { id: 'issue-2', title: 'Second Issue' } },
    ])

    render(<StaticTaskGraphView data={data} selectedIssueId="issue-1" />)

    const rows = screen.getAllByTestId('static-task-graph-issue-row')
    expect(rows[0]).toHaveClass('ring-2')
    expect(rows[0]).toHaveClass('ring-primary')
    expect(rows[1]).not.toHaveClass('ring-2')
  })

  it('calls onSelectIssue when row clicked', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'First Issue' } }])
    const onSelectIssue = vi.fn()

    render(<StaticTaskGraphView data={data} onSelectIssue={onSelectIssue} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    fireEvent.click(row)

    expect(onSelectIssue).toHaveBeenCalledWith('issue-1')
  })

  it('shows cursor-pointer when onSelectIssue provided', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'First Issue' } }])
    const onSelectIssue = vi.fn()

    render(<StaticTaskGraphView data={data} onSelectIssue={onSelectIssue} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    expect(row).toHaveClass('cursor-pointer')
  })

  it('does not show cursor-pointer when onSelectIssue not provided', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'First Issue' } }])

    render(<StaticTaskGraphView data={data} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    expect(row).not.toHaveClass('cursor-pointer')
  })

  it('supports keyboard navigation with Enter key', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'First Issue' } }])
    const onSelectIssue = vi.fn()

    render(<StaticTaskGraphView data={data} onSelectIssue={onSelectIssue} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    fireEvent.keyDown(row, { key: 'Enter' })

    expect(onSelectIssue).toHaveBeenCalledWith('issue-1')
  })

  it('supports keyboard navigation with Space key', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'First Issue' } }])
    const onSelectIssue = vi.fn()

    render(<StaticTaskGraphView data={data} onSelectIssue={onSelectIssue} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    fireEvent.keyDown(row, { key: ' ' })

    expect(onSelectIssue).toHaveBeenCalledWith('issue-1')
  })

  it('has button role and tabindex when clickable', () => {
    const data = createMockTaskGraph([{ issue: { id: 'issue-1', title: 'First Issue' } }])
    const onSelectIssue = vi.fn()

    render(<StaticTaskGraphView data={data} onSelectIssue={onSelectIssue} />)

    const row = screen.getByTestId('static-task-graph-issue-row')
    expect(row).toHaveAttribute('role', 'button')
    expect(row).toHaveAttribute('tabindex', '0')
  })
})
