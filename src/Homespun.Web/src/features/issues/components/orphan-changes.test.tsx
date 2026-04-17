import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { BranchOrphanList, MainOrphanList } from './orphan-changes'
import { ChangeSnapshot, Issues } from '@/api'
import type { IssueResponse } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    ChangeSnapshot: { postApiOpenspecChangesLink: vi.fn() },
    Issues: { postApiIssues: vi.fn() },
  }
})

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

describe('BranchOrphanList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockLink.mockResolvedValue(okResponse(undefined))
    mockCreate.mockResolvedValue(
      okResponse<IssueResponse>({ id: 'new-issue-1', title: 'OpenSpec: foo' } as IssueResponse)
    )
  })

  it('renders nothing when orphans empty', () => {
    const { container } = render(
      <BranchOrphanList projectId="p1" branch="b" fleeceId="f" orphans={[]} />,
      { wrapper: wrapper() }
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('renders one entry per orphan with actions', () => {
    render(
      <BranchOrphanList
        projectId="p1"
        branch="feat/x+f"
        fleeceId="f"
        orphans={[{ name: 'foo', createdOnBranch: true }]}
      />,
      { wrapper: wrapper() }
    )
    const row = screen.getByTestId('branch-orphan')
    expect(row).toHaveAttribute('data-change-name', 'foo')
    expect(within(row).getByTestId('orphan-link-to-issue')).toBeInTheDocument()
    expect(within(row).getByTestId('orphan-create-sub-issue')).toBeInTheDocument()
  })

  it('link-to-issue posts sidecar with branch fleece-id', async () => {
    const user = userEvent.setup()
    render(
      <BranchOrphanList
        projectId="p1"
        branch="feat/x+f"
        fleeceId="f"
        orphans={[{ name: 'foo', createdOnBranch: true }]}
      />,
      { wrapper: wrapper() }
    )

    await user.click(screen.getByTestId('orphan-link-to-issue'))

    await waitFor(() => {
      expect(mockLink).toHaveBeenCalledWith({
        body: {
          projectId: 'p1',
          branch: 'feat/x+f',
          changeName: 'foo',
          fleeceId: 'f',
        },
      })
    })
  })

  it('create-sub-issue creates child + links to new issue id', async () => {
    const user = userEvent.setup()
    render(
      <BranchOrphanList
        projectId="p1"
        branch="feat/x+f"
        fleeceId="f"
        orphans={[{ name: 'foo', createdOnBranch: true }]}
      />,
      { wrapper: wrapper() }
    )

    await user.click(screen.getByTestId('orphan-create-sub-issue'))

    await waitFor(() => expect(mockCreate).toHaveBeenCalled())
    const createCall = mockCreate.mock.calls[0]?.[0]
    expect(createCall?.body).toMatchObject({
      projectId: 'p1',
      title: 'OpenSpec: foo',
      parentIssueId: 'f',
    })
    await waitFor(() =>
      expect(mockLink).toHaveBeenCalledWith({
        body: expect.objectContaining({ fleeceId: 'new-issue-1', changeName: 'foo' }),
      })
    )
  })
})

describe('MainOrphanList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockLink.mockResolvedValue(okResponse(undefined))
    mockCreate.mockResolvedValue(
      okResponse<IssueResponse>({ id: 'issue-42', title: 'OpenSpec: bar' } as IssueResponse)
    )
  })

  it('renders nothing when orphans empty', () => {
    const { container } = render(<MainOrphanList projectId="p1" orphans={[]} />, {
      wrapper: wrapper(),
    })
    expect(container).toBeEmptyDOMElement()
  })

  it('renders Orphaned Changes header + create-issue action', async () => {
    const user = userEvent.setup()
    render(<MainOrphanList projectId="p1" orphans={[{ name: 'bar', createdOnBranch: false }]} />, {
      wrapper: wrapper(),
    })

    expect(screen.getByTestId('main-orphans-section')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /orphaned changes/i })).toBeInTheDocument()

    await user.click(screen.getByTestId('orphan-create-issue'))

    await waitFor(() => expect(mockCreate).toHaveBeenCalled())
    const createCall = mockCreate.mock.calls[0]?.[0]
    expect(createCall?.body).toMatchObject({ projectId: 'p1', title: 'OpenSpec: bar' })
    await waitFor(() =>
      expect(mockLink).toHaveBeenCalledWith({
        body: { projectId: 'p1', branch: null, changeName: 'bar', fleeceId: 'issue-42' },
      })
    )
  })
})
