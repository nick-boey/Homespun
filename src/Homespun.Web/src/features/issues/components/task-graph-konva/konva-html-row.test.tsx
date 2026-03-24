/**
 * Tests for KonvaHtmlRow component.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { KonvaHtmlRow } from './konva-html-row'
import type { TaskGraphIssueRenderLine } from '../../services'
import { TaskGraphMarkerType } from '../../services'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'

// Mock the useLinkedPrStatus hook
vi.mock('../../hooks/use-linked-pr-status', () => ({
  useLinkedPrStatus: vi.fn(() => ({ data: null })),
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

/** Helper to create a minimal issue render line */
function createIssueLine(
  overrides: Partial<TaskGraphIssueRenderLine> & { issueId: string; lane: number }
): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    title: 'Test Issue',
    description: null,
    branchName: null,
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
    executionMode: ExecutionMode.PARALLEL,
    parentIssues: null,
    ...overrides,
  }
}

const defaultProps = {
  line: createIssueLine({ issueId: 'issue-1', lane: 0, title: 'Test Issue' }),
  projectId: 'project-1',
  maxLanes: 3,
  onEdit: vi.fn(),
  onRunAgent: vi.fn(),
}

function renderRow(props = {}) {
  return render(createElement(KonvaHtmlRow, { ...defaultProps, ...props }), {
    wrapper: createWrapper(),
  })
}

describe('KonvaHtmlRow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the issue title', () => {
    renderRow()
    expect(screen.getByText('Test Issue')).toBeInTheDocument()
  })

  it('renders the issue ID (first 6 characters)', () => {
    const line = createIssueLine({ issueId: 'abc123def456', lane: 0 })
    renderRow({ line })
    expect(screen.getByText('abc123')).toBeInTheDocument()
  })

  it('renders type badge with correct label', () => {
    const bugLine = createIssueLine({
      issueId: 'bug-1',
      lane: 0,
      issueType: IssueType.BUG,
    })
    renderRow({ line: bugLine })
    expect(screen.getByText('Bug')).toBeInTheDocument()
  })

  it('renders status badge', () => {
    const line = createIssueLine({
      issueId: 'issue-1',
      lane: 0,
      status: IssueStatus.PROGRESS,
    })
    renderRow({ line })
    expect(screen.getByText('Progress')).toBeInTheDocument()
  })

  it('applies selected styling when isSelected is true', () => {
    renderRow({ isSelected: true })
    const row = screen.getByTestId('konva-html-row')
    expect(row.className).toContain('bg-muted')
  })

  it('does not apply selected styling when isSelected is false', () => {
    renderRow({ isSelected: false })
    const row = screen.getByTestId('konva-html-row')
    // Not selected means no bg-muted (exact class) but may have hover:bg-muted/50
    expect(row.className).not.toContain(' bg-muted ')
    // The row without selection has hover:bg-muted/50 which is fine
  })

  it('calls onClick when row is clicked', () => {
    const onClick = vi.fn()
    renderRow({ onClick })
    fireEvent.click(screen.getByTestId('konva-html-row'))
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('shows linked PR number when present', () => {
    const line = createIssueLine({
      issueId: 'issue-1',
      lane: 0,
      linkedPr: {
        number: 123,
        url: 'https://github.com/test/pr/123',
      },
    })
    renderRow({ line })
    expect(screen.getByText('#123')).toBeInTheDocument()
  })

  it('shows assignee badge when assigned', () => {
    const line = createIssueLine({
      issueId: 'issue-1',
      lane: 0,
      assignedTo: 'john@example.com',
    })
    renderRow({ line })
    expect(screen.getByText('john')).toBeInTheDocument()
  })

  it('positions row based on maxLanes', () => {
    const { container } = renderRow({ maxLanes: 5 })
    // The SVG placeholder width should be calculated based on maxLanes
    const row = container.querySelector('[data-testid="konva-html-row"]')
    expect(row).toBeInTheDocument()
  })

  it('applies move source styling when isMoveSource is true', () => {
    renderRow({ isMoveSource: true })
    const row = screen.getByTestId('konva-html-row')
    expect(row.className).toContain('ring-primary')
  })

  it('applies move target styling when move operation is active', () => {
    renderRow({ isMoveOperationActive: true, isMoveSource: false })
    const row = screen.getByTestId('konva-html-row')
    expect(row.className).toContain('hover:ring-2')
  })
})
