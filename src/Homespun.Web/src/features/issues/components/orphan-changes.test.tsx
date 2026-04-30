import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { OrphanedChangesList } from './orphan-changes'
import { ChangeSnapshot, Issues, ExecutionMode, IssueStatus, IssueType } from '@/api'
import type { IssueResponse } from '@/api/generated/types.gen'
import type { OrphanEntry } from '../services/orphan-aggregation'
import type { TaskGraphIssueRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    ChangeSnapshot: { postApiOpenspecChangesLink: vi.fn() },
    Issues: { postApiIssues: vi.fn() },
  }
})

vi.mock('../hooks/use-linked-pr-status', () => ({
  useLinkedPrStatus: () => ({ data: null }),
}))

const mockLink = vi.mocked(ChangeSnapshot.postApiOpenspecChangesLink)
const mockCreate = vi.mocked(Issues.postApiIssues)

function okResponse<T>(data: T) {
  return {
    data,
    error: undefined,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function wrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
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
  }
}

const issues: TaskGraphIssueRenderLine[] = [
  makeIssue('i1', 'Issue One'),
  makeIssue('i2', 'Issue Two'),
  makeIssue('i3', 'Issue Three'),
]

describe('OrphanedChangesList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockLink.mockResolvedValue(okResponse(undefined))
    mockCreate.mockResolvedValue(okResponse<IssueResponse>({ id: 'new-issue-1' } as IssueResponse))
  })

  it('renders nothing when entries empty', () => {
    const { container } = render(
      <OrphanedChangesList projectId="p1" entries={[]} issues={issues} />,
      { wrapper: wrapper() }
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('T012.1 renders one row per deduped change name', () => {
    const entries: OrphanEntry[] = [
      {
        name: 'alpha-change',
        occurrences: [{ branch: null, changeName: 'alpha-change' }],
        containingIssueIds: [],
      },
      {
        name: 'beta-change',
        occurrences: [
          { branch: null, changeName: 'beta-change' },
          { branch: 'feat/x+i1', changeName: 'beta-change' },
        ],
        containingIssueIds: ['i1'],
      },
    ]

    render(<OrphanedChangesList projectId="p1" entries={entries} issues={issues} />, {
      wrapper: wrapper(),
    })

    expect(screen.getByTestId('orphaned-changes-section')).toBeInTheDocument()
    const rows = screen.getAllByTestId('orphaned-change-row')
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveAttribute('data-change-name', 'alpha-change')
    expect(rows[1]).toHaveAttribute('data-change-name', 'beta-change')
  })

  it('T012.2a single-occurrence main shows "main" label', () => {
    const entries: OrphanEntry[] = [
      {
        name: 'alpha',
        occurrences: [{ branch: null, changeName: 'alpha' }],
        containingIssueIds: [],
      },
    ]

    render(<OrphanedChangesList projectId="p1" entries={entries} issues={issues} />, {
      wrapper: wrapper(),
    })
    const row = screen.getByTestId('orphaned-change-row')
    expect(within(row).getByText('main')).toBeInTheDocument()
  })

  it('T012.2b single-occurrence branch shows branch name', () => {
    const entries: OrphanEntry[] = [
      {
        name: 'alpha',
        occurrences: [{ branch: 'feat/x+i1', changeName: 'alpha' }],
        containingIssueIds: ['i1'],
      },
    ]

    render(<OrphanedChangesList projectId="p1" entries={entries} issues={issues} />, {
      wrapper: wrapper(),
    })
    const row = screen.getByTestId('orphaned-change-row')
    expect(within(row).getByText('feat/x+i1')).toBeInTheDocument()
  })

  it('T012.2c multi-occurrence shows "on N branches" with tooltip listing all', async () => {
    const user = userEvent.setup()
    const entries: OrphanEntry[] = [
      {
        name: 'beta',
        occurrences: [
          { branch: null, changeName: 'beta' },
          { branch: 'feat/x+i1', changeName: 'beta' },
          { branch: 'feat/y+i2', changeName: 'beta' },
        ],
        containingIssueIds: ['i1', 'i2'],
      },
    ]

    render(<OrphanedChangesList projectId="p1" entries={entries} issues={issues} />, {
      wrapper: wrapper(),
    })
    const row = screen.getByTestId('orphaned-change-row')
    const label = within(row).getByTestId('orphaned-change-occurrence-label')
    expect(label).toHaveTextContent('on 3 branches')

    await user.hover(label)
    const tooltip = await screen.findByRole('tooltip')
    expect(tooltip).toHaveTextContent('main')
    expect(tooltip).toHaveTextContent('feat/x+i1')
    expect(tooltip).toHaveTextContent('feat/y+i2')
  })

  it('T012.3 link-to-issue opens picker pre-populated with containingIssueIds', async () => {
    const user = userEvent.setup()
    const entries: OrphanEntry[] = [
      {
        name: 'alpha',
        occurrences: [{ branch: 'feat/x+i1', changeName: 'alpha' }],
        containingIssueIds: ['i1'],
      },
    ]

    render(<OrphanedChangesList projectId="p1" entries={entries} issues={issues} />, {
      wrapper: wrapper(),
    })

    await user.click(screen.getByTestId('orphan-link-to-issue'))

    const pinned = await screen.findByTestId('orphan-picker-pinned')
    expect(within(pinned).getByText('Issue One')).toBeInTheDocument()
  })

  it('T012.3b selecting an issue in picker emits a single branchless link call with the selected fleeceId', async () => {
    const user = userEvent.setup()
    const entries: OrphanEntry[] = [
      {
        name: 'beta',
        occurrences: [
          { branch: null, changeName: 'beta' },
          { branch: 'feat/x+i1', changeName: 'beta' },
        ],
        containingIssueIds: ['i1'],
      },
    ]

    render(<OrphanedChangesList projectId="p1" entries={entries} issues={issues} />, {
      wrapper: wrapper(),
    })

    await user.click(screen.getByTestId('orphan-link-to-issue'))
    await user.click(await screen.findByTestId('orphan-picker-row-i2'))

    await waitFor(() => expect(mockLink).toHaveBeenCalledTimes(1))
    expect(mockLink.mock.calls[0]?.[0]?.body).toEqual({
      projectId: 'p1',
      changeName: 'beta',
      fleeceId: 'i2',
    })
  })

  it('T012.4a split-button primary creates issue then issues a single branchless link call', async () => {
    const user = userEvent.setup()
    const entries: OrphanEntry[] = [
      {
        name: 'alpha',
        occurrences: [
          { branch: null, changeName: 'alpha' },
          { branch: 'feat/x+i1', changeName: 'alpha' },
        ],
        containingIssueIds: ['i1'],
      },
    ]

    render(<OrphanedChangesList projectId="p1" entries={entries} issues={issues} />, {
      wrapper: wrapper(),
    })

    await user.click(screen.getByTestId('orphan-create-issue'))

    await waitFor(() => expect(mockCreate).toHaveBeenCalled())
    expect(mockCreate.mock.calls[0]?.[0]?.body).toMatchObject({
      projectId: 'p1',
      title: 'OpenSpec: alpha',
    })
    expect(mockCreate.mock.calls[0]?.[0]?.body?.parentIssueId).toBeUndefined()

    await waitFor(() => expect(mockLink).toHaveBeenCalledTimes(1))
    expect(mockLink.mock.calls[0]?.[0]?.body).toEqual({
      projectId: 'p1',
      changeName: 'alpha',
      fleeceId: 'new-issue-1',
    })
  })

  it('T012.4b split-button dropdown opens picker in choose-parent mode; selected parent passed to createIssue', async () => {
    const user = userEvent.setup()
    const entries: OrphanEntry[] = [
      {
        name: 'alpha',
        occurrences: [{ branch: null, changeName: 'alpha' }],
        containingIssueIds: [],
      },
    ]

    render(<OrphanedChangesList projectId="p1" entries={entries} issues={issues} />, {
      wrapper: wrapper(),
    })

    await user.click(screen.getByTestId('orphan-create-issue-menu'))
    await user.click(await screen.findByTestId('orphan-create-sub-issue-menuitem'))

    // Picker opens with 'choose parent' mode.
    await user.click(await screen.findByTestId('orphan-picker-row-i2'))

    await waitFor(() => expect(mockCreate).toHaveBeenCalled())
    expect(mockCreate.mock.calls[0]?.[0]?.body).toMatchObject({
      projectId: 'p1',
      title: 'OpenSpec: alpha',
      parentIssueId: 'i2',
    })
    await waitFor(() =>
      expect(mockLink).toHaveBeenCalledWith({
        body: { projectId: 'p1', changeName: 'alpha', fleeceId: 'new-issue-1' },
      })
    )
  })
})
