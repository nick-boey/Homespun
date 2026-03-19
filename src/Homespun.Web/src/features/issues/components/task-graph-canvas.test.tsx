import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { TaskGraphCanvas } from './task-graph-canvas'
import type { TaskGraphIssueRenderLine, TaskGraphPrRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'

// Mock the hooks that make network calls
vi.mock('../hooks/use-linked-pr-status', () => ({
  useLinkedPrStatus: () => ({ data: null }),
}))

function createIssueRenderLine(
  overrides: Partial<TaskGraphIssueRenderLine> = {}
): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    issueId: 'test-123',
    title: 'Test Issue',
    description: null,
    branchName: null,
    lane: 0,
    marker: TaskGraphMarkerType.Open,
    parentLane: null,
    isFirstChild: false,
    isSeriesChild: false,
    drawTopLine: false,
    drawBottomLine: false,
    seriesConnectorFromLane: null,
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
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
    ...overrides,
  }
}

function createPrRenderLine(overrides: Partial<TaskGraphPrRenderLine> = {}): TaskGraphPrRenderLine {
  return {
    type: 'pr',
    prNumber: 123,
    title: 'Test PR',
    url: null,
    isMerged: true,
    hasDescription: false,
    agentStatus: null,
    drawTopLine: false,
    drawBottomLine: false,
    ...overrides,
  }
}

function renderWithQueryClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })

  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>)
}

describe('TaskGraphCanvas', () => {
  it('renders the canvas SVG element', () => {
    const renderLines = [createIssueRenderLine()]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
      />
    )

    const svg = screen.getByTestId('task-graph-canvas')
    expect(svg).toBeInTheDocument()
    expect(svg.tagName.toLowerCase()).toBe('svg')
  })

  it('renders issue nodes in the SVG', () => {
    const renderLines = [
      createIssueRenderLine({ issueId: 'issue-1', title: 'First Issue' }),
      createIssueRenderLine({ issueId: 'issue-2', title: 'Second Issue' }),
    ]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
      />
    )

    // Check that the issues are rendered
    expect(screen.getByText('First Issue')).toBeInTheDocument()
    expect(screen.getByText('Second Issue')).toBeInTheDocument()
  })

  it('renders PR nodes', () => {
    const renderLines = [createPrRenderLine({ prNumber: 456, title: 'Test PR' })]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
      />
    )

    expect(screen.getByText('Test PR')).toBeInTheDocument()
    expect(screen.getByText('#456')).toBeInTheDocument()
  })

  it('renders edge paths based on layout', () => {
    const renderLines = [
      createIssueRenderLine({
        issueId: 'issue-1',
        drawTopLine: true,
        drawBottomLine: true,
      }),
    ]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
      />
    )

    // Check that edges are rendered (path elements)
    const svg = screen.getByTestId('task-graph-canvas')
    const paths = svg.querySelectorAll('path.edge')
    expect(paths.length).toBeGreaterThan(0)
  })

  it('highlights selected issue', () => {
    const renderLines = [createIssueRenderLine({ issueId: 'issue-1' })]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
        selectedIssueId="issue-1"
      />
    )

    const issueContent = screen.getByRole('row', { selected: true })
    expect(issueContent).toBeInTheDocument()
  })

  it('calls onSelectIssue when clicking an issue', async () => {
    const onSelectIssue = vi.fn()
    const renderLines = [createIssueRenderLine({ issueId: 'issue-1', title: 'Click Me' })]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
        onSelectIssue={onSelectIssue}
      />
    )

    const issueRow = screen.getByRole('row')
    await userEvent.click(issueRow)

    expect(onSelectIssue).toHaveBeenCalledWith('issue-1')
  })

  it('renders expanded content when issue is in expandedIds', () => {
    const renderLines = [
      createIssueRenderLine({
        issueId: 'issue-1',
        description: 'Test description content',
      }),
    ]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set(['issue-1'])}
      />
    )

    // The expanded content should be visible
    const expandedRow = screen.getByRole('row', { expanded: true })
    expect(expandedRow).toBeInTheDocument()
  })

  it('calculates correct total height based on render lines', () => {
    const renderLines = [
      createIssueRenderLine({ issueId: 'issue-1' }),
      createIssueRenderLine({ issueId: 'issue-2' }),
      createIssueRenderLine({ issueId: 'issue-3' }),
    ]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
      />
    )

    const svg = screen.getByTestId('task-graph-canvas')
    // Each row is ROW_HEIGHT (40px), so 3 rows = 120px
    expect(svg.getAttribute('height')).toBe('120')
  })

  it('renders separator row correctly', () => {
    const renderLines = [
      createPrRenderLine({ prNumber: 1 }),
      { type: 'separator' as const },
      createIssueRenderLine({ issueId: 'issue-1' }),
    ]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
      />
    )

    const separator = screen.getByRole('separator')
    expect(separator).toBeInTheDocument()
  })

  it('renders load more button', () => {
    const renderLines = [{ type: 'loadMore' as const }]

    renderWithQueryClient(
      <TaskGraphCanvas
        renderLines={renderLines}
        maxLanes={1}
        projectId="test-project"
        expandedIds={new Set()}
      />
    )

    expect(screen.getByText('Load more PRs...')).toBeInTheDocument()
  })
})
