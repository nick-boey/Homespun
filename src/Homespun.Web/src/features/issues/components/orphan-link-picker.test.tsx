import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { ExecutionMode, IssueStatus, IssueType } from '@/api'
import { OrphanLinkPicker } from './orphan-link-picker'
import type { TaskGraphIssueRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'

vi.mock('../hooks/use-linked-pr-status', () => ({
  useLinkedPrStatus: () => ({ data: null }),
}))

function wrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
}

function makeIssue(id: string, title: string): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    issueId: id,
    title,
    description: null,
    branchName: null,
    lane: 0,
    marker: TaskGraphMarkerType.Actionable,
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
    parentIssues: null,
    multiParentIndex: null,
    multiParentTotal: null,
    isLastChild: false,
    hasParallelChildren: false,
    parentIssueId: null,
    parentLaneReservations: [],
  }
}

const issues: TaskGraphIssueRenderLine[] = [
  makeIssue('a1', 'Alpha implementation'),
  makeIssue('b2', 'Bravo refactor'),
  makeIssue('c3', 'Charlie bug fix'),
]

describe('OrphanLinkPicker', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('T008.1 renders filter input, pinned block, divider, and full issue list', () => {
    render(
      <OrphanLinkPicker
        open
        onOpenChange={() => {}}
        title="Link orphan"
        projectId="p1"
        issues={issues}
        containingIssueIds={['a1']}
        onSelect={() => {}}
      />,
      { wrapper: wrapper() }
    )

    expect(screen.getByTestId('orphan-picker-filter')).toBeInTheDocument()
    const pinned = screen.getByTestId('orphan-picker-pinned')
    expect(within(pinned).getByText('Alpha implementation')).toBeInTheDocument()
    expect(screen.getByTestId('orphan-picker-divider')).toBeInTheDocument()
    const list = screen.getByTestId('orphan-picker-list')
    // Full issue list below the divider shows all three.
    expect(within(list).getByText('Alpha implementation')).toBeInTheDocument()
    expect(within(list).getByText('Bravo refactor')).toBeInTheDocument()
    expect(within(list).getByText('Charlie bug fix')).toBeInTheDocument()
  })

  it('T008.2 fuzzy filter narrows lower list; pinned block unchanged', async () => {
    const user = userEvent.setup()
    render(
      <OrphanLinkPicker
        open
        onOpenChange={() => {}}
        title="Link orphan"
        projectId="p1"
        issues={issues}
        containingIssueIds={['a1']}
        onSelect={() => {}}
      />,
      { wrapper: wrapper() }
    )

    await user.type(screen.getByTestId('orphan-picker-filter'), 'brav')

    const pinned = screen.getByTestId('orphan-picker-pinned')
    expect(within(pinned).getByText('Alpha implementation')).toBeInTheDocument()

    const list = screen.getByTestId('orphan-picker-list')
    expect(within(list).queryByText('Alpha implementation')).not.toBeInTheDocument()
    expect(within(list).queryByText('Charlie bug fix')).not.toBeInTheDocument()
    expect(within(list).getByText('Bravo refactor')).toBeInTheDocument()
  })

  it('T008.3 highlighted issue appears in both pinned block and lower list regardless of filter', async () => {
    const user = userEvent.setup()
    render(
      <OrphanLinkPicker
        open
        onOpenChange={() => {}}
        title="Link orphan"
        projectId="p1"
        issues={issues}
        containingIssueIds={['a1']}
        onSelect={() => {}}
      />,
      { wrapper: wrapper() }
    )

    // Filter state matching pinned issue.
    await user.type(screen.getByTestId('orphan-picker-filter'), 'alpha')

    expect(
      within(screen.getByTestId('orphan-picker-pinned')).getByText('Alpha implementation')
    ).toBeInTheDocument()
    expect(
      within(screen.getByTestId('orphan-picker-list')).getByText('Alpha implementation')
    ).toBeInTheDocument()

    // Clear filter — still appears in both sections.
    await user.clear(screen.getByTestId('orphan-picker-filter'))
    expect(
      within(screen.getByTestId('orphan-picker-pinned')).getByText('Alpha implementation')
    ).toBeInTheDocument()
    expect(
      within(screen.getByTestId('orphan-picker-list')).getByText('Alpha implementation')
    ).toBeInTheDocument()
  })

  it('T008.4 clicking a row invokes onSelect and closes the dialog', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    const onOpenChange = vi.fn()

    render(
      <OrphanLinkPicker
        open
        onOpenChange={onOpenChange}
        title="Link orphan"
        projectId="p1"
        issues={issues}
        containingIssueIds={[]}
        onSelect={onSelect}
      />,
      { wrapper: wrapper() }
    )

    const row = screen.getByTestId('orphan-picker-row-b2')
    await user.click(row)

    expect(onSelect).toHaveBeenCalledWith('b2')
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('T008.5 empty state: zero issues and zero matches', async () => {
    const user = userEvent.setup()
    const { rerender } = render(
      <OrphanLinkPicker
        open
        onOpenChange={() => {}}
        title="Link orphan"
        projectId="p1"
        issues={[]}
        containingIssueIds={[]}
        onSelect={() => {}}
      />,
      { wrapper: wrapper() }
    )
    expect(screen.getByText(/no issues/i)).toBeInTheDocument()

    rerender(
      <OrphanLinkPicker
        open
        onOpenChange={() => {}}
        title="Link orphan"
        projectId="p1"
        issues={issues}
        containingIssueIds={[]}
        onSelect={() => {}}
      />
    )
    await user.type(screen.getByTestId('orphan-picker-filter'), 'no-such-issue-xyz')
    expect(screen.getByText(/no matches/i)).toBeInTheDocument()
  })

  it('does not render a pinned block when containingIssueIds is empty', () => {
    render(
      <OrphanLinkPicker
        open
        onOpenChange={() => {}}
        title="Link orphan"
        projectId="p1"
        issues={issues}
        containingIssueIds={[]}
        onSelect={() => {}}
      />,
      { wrapper: wrapper() }
    )
    expect(screen.queryByTestId('orphan-picker-pinned')).not.toBeInTheDocument()
    expect(screen.queryByTestId('orphan-picker-divider')).not.toBeInTheDocument()
  })
})
