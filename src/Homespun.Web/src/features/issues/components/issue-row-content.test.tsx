import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { BranchPresence, ChangePhase, ExecutionMode, IssueStatus, IssueType } from '@/api'
import type { IssueOpenSpecState } from '@/api/generated/types.gen'
import { IssueRowContent } from './issue-row-content'
import type { TaskGraphIssueRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'

// Avoid live PR-status fetches in tests.
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

function makeLine(overrides: Partial<TaskGraphIssueRenderLine> = {}): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    issueId: 'i1',
    title: 'Sample issue title',
    description: null,
    branchName: null,
    lane: 0,
    marker: TaskGraphMarkerType.Actionable,
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
    hasDescription: false,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    executionMode: ExecutionMode.SERIES,
    parentIssues: null,
    appearanceIndex: 1,
    totalAppearances: 1,
    parentIssueId: null,
    ...overrides,
  }
}

const baseOpenSpecState: IssueOpenSpecState = {
  branchState: BranchPresence.WITH_CHANGE,
  changeState: ChangePhase.INCOMPLETE,
  changeName: 'add-feature-x',
  schemaName: 'spec-driven',
  phases: [
    { name: 'Phase 1', total: 3, done: 1 },
    { name: 'Phase 2', total: 2, done: 2 },
  ],
  orphans: null,
}

describe('IssueRowContent', () => {
  it('renders title, type pill, status pill, openspec indicators, exec-mode and assignee', () => {
    render(
      <IssueRowContent
        line={makeLine({ assignedTo: 'dev@example.com' })}
        projectId="p1"
        openSpecState={baseOpenSpecState}
        editable
      />,
      { wrapper: wrapper() }
    )

    expect(screen.getByText('Sample issue title')).toBeInTheDocument()
    // Type pill — "Task" label
    expect(screen.getByRole('button', { name: /task/i })).toBeInTheDocument()
    // Status pill — "Open" compact label
    expect(screen.getByRole('button', { name: /open/i })).toBeInTheDocument()
    // OpenSpec indicators mount
    expect(screen.getByTestId('openspec-indicators')).toBeInTheDocument()
    // Phase rollup badges are no longer rendered inline (phases appear as graph rows)
    // Execution-mode toggle present
    expect(
      screen.getByRole('button', { name: /series execution mode|parallel execution mode/i })
    ).toBeInTheDocument()
    // Assignee badge (display name portion)
    expect(screen.getByText('dev')).toBeInTheDocument()
  })

  it('renders linked PR number when line has a linkedPr', () => {
    render(
      <IssueRowContent
        line={makeLine({
          linkedPr: { number: 42, url: 'https://example.com/pr/42' },
        })}
        projectId="p1"
        editable
      />,
      { wrapper: wrapper() }
    )
    expect(screen.getByRole('link', { name: /#42/ })).toBeInTheDocument()
  })

  it('editable=false renders type/status as static pills and exec-mode is disabled', async () => {
    const onTypeChange = vi.fn()
    const onStatusChange = vi.fn()
    const onExecutionModeChange = vi.fn()
    const user = userEvent.setup()

    render(
      <IssueRowContent
        line={makeLine()}
        projectId="p1"
        editable={false}
        onTypeChange={onTypeChange}
        onStatusChange={onStatusChange}
        onExecutionModeChange={onExecutionModeChange}
      />,
      { wrapper: wrapper() }
    )

    const typePill = screen.getByTestId('issue-row-type-pill')
    const statusPill = screen.getByTestId('issue-row-status-pill')
    // Static pills are not interactive buttons.
    expect(typePill.tagName).not.toBe('BUTTON')
    expect(statusPill.tagName).not.toBe('BUTTON')

    // Clicking a pill does not invoke change handlers.
    await user.click(typePill)
    await user.click(statusPill)
    expect(onTypeChange).not.toHaveBeenCalled()
    expect(onStatusChange).not.toHaveBeenCalled()

    // Exec-mode toggle exists but is disabled when not editable.
    const execToggle = screen.getByRole('button', {
      name: /series execution mode|parallel execution mode/i,
    })
    expect(execToggle).toBeDisabled()
    await user.click(execToggle)
    expect(onExecutionModeChange).not.toHaveBeenCalled()
  })

  it('editable=true calls onTypeChange when a type menu item is clicked', async () => {
    const onTypeChange = vi.fn()
    const user = userEvent.setup()

    render(
      <IssueRowContent line={makeLine()} projectId="p1" editable onTypeChange={onTypeChange} />,
      { wrapper: wrapper() }
    )

    const typeButton = screen.getByRole('button', { name: /task/i })
    await user.click(typeButton)
    // A menu opens with other types — pick one.
    const bugItem = await screen.findByRole('menuitem', { name: /bug/i })
    await user.click(bugItem)
    expect(onTypeChange).toHaveBeenCalledWith('i1', IssueType.BUG)
  })

  it('hides the multi-parent badge when editable=false (picker shell)', () => {
    const line = makeLine({ appearanceIndex: 1, totalAppearances: 2 })
    const { rerender } = render(<IssueRowContent line={line} projectId="p1" editable />, {
      wrapper: wrapper(),
    })
    expect(screen.queryByTestId('multi-parent-badge')).toBeInTheDocument()

    rerender(<IssueRowContent line={line} projectId="p1" editable={false} />)
    expect(screen.queryByTestId('multi-parent-badge')).not.toBeInTheDocument()
  })

  it('renders the execution-mode toggle region with an accessible label', () => {
    render(<IssueRowContent line={makeLine()} projectId="p1" editable />, { wrapper: wrapper() })
    const toggle = screen.getByRole('button', {
      name: /series execution mode|parallel execution mode/i,
    })
    expect(toggle).toBeInTheDocument()
  })
})
