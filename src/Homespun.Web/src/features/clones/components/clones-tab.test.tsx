import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ClonesTab } from './clones-tab'
import type { EnrichedCloneInfo } from '@/api/generated/types.gen'

// Mock hooks
const mockRefetch = vi.fn()
let mockEnrichedClonesReturn: {
  data: EnrichedCloneInfo[] | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
} = {
  data: undefined,
  isLoading: false,
  isError: false,
  error: null,
  refetch: mockRefetch,
}

vi.mock('../hooks/use-enriched-clones', () => ({
  useEnrichedClones: () => mockEnrichedClonesReturn,
}))

const mockDeleteCloneMutateAsync = vi.fn()
vi.mock('@/features/branches/hooks/use-clones', () => ({
  useDeleteClone: () => ({
    mutateAsync: mockDeleteCloneMutateAsync,
    isPending: false,
  }),
}))

const mockBulkDeleteMutateAsync = vi.fn()
vi.mock('../hooks/use-bulk-delete-clones', () => ({
  useBulkDeleteClones: () => ({
    mutateAsync: mockBulkDeleteMutateAsync,
    isPending: false,
  }),
}))

const createMockClone = (overrides: Partial<EnrichedCloneInfo> = {}): EnrichedCloneInfo => ({
  clone: {
    path: '/repos/.clones/feature+test-branch',
    workdirPath: '/repos/.clones/feature+test-branch/workdir',
    branch: 'refs/heads/feature/test-branch',
    headCommit: 'abc1234567890',
    isBare: false,
    isDetached: false,
    expectedBranch: 'feature/test-branch',
    folderName: 'feature+test-branch',
  },
  linkedIssueId: null,
  linkedIssue: undefined,
  linkedPr: undefined,
  isDeletable: false,
  deletionReason: null,
  isIssuesAgentClone: false,
  ...overrides,
})

// Mock EnrichedCloneCard to simplify testing
vi.mock('./enriched-clone-card', () => ({
  EnrichedCloneCard: ({
    clone,
    onDelete,
    isDeleting,
  }: {
    clone: EnrichedCloneInfo
    projectId: string
    onDelete: () => void
    isDeleting?: boolean
  }) => (
    <div data-testid={`clone-card-${clone.clone.path}`}>
      <span>{clone.clone.expectedBranch}</span>
      <button onClick={onDelete} disabled={isDeleting} data-testid={`delete-${clone.clone.path}`}>
        Delete
      </button>
    </div>
  ),
}))

// Mock TanStack Router
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: { children: React.ReactNode }) => <a {...props}>{children}</a>,
}))

describe('ClonesTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockDeleteCloneMutateAsync.mockResolvedValue(undefined)
    mockBulkDeleteMutateAsync.mockResolvedValue({ results: [] })
    mockEnrichedClonesReturn = {
      data: undefined,
      isLoading: false,
      isError: false,
      error: null,
      refetch: mockRefetch,
    }
  })

  it('renders loading skeleton while fetching', () => {
    mockEnrichedClonesReturn = {
      ...mockEnrichedClonesReturn,
      isLoading: true,
    }

    const { container } = render(<ClonesTab projectId="project-1" />)

    // Skeleton should render pulse animations
    const skeletons = container.querySelectorAll('.animate-pulse')
    expect(skeletons.length).toBeGreaterThan(0)
  })

  it('renders error state on fetch failure', () => {
    mockEnrichedClonesReturn = {
      ...mockEnrichedClonesReturn,
      isError: true,
      error: new Error('Network error'),
    }

    render(<ClonesTab projectId="project-1" />)

    expect(screen.getByText('Failed to load clones')).toBeInTheDocument()
  })

  it('renders empty state when no clones', () => {
    mockEnrichedClonesReturn = {
      ...mockEnrichedClonesReturn,
      data: [],
    }

    render(<ClonesTab projectId="project-1" />)

    expect(screen.getByText('No Clones Found')).toBeInTheDocument()
    expect(
      screen.getByText('Clones will appear here when you create sessions on branches.')
    ).toBeInTheDocument()
  })

  it('separates clones into Feature and Issues Agent sections', () => {
    const featureClone = createMockClone({
      clone: {
        ...createMockClone().clone,
        path: '/repos/.clones/feature+a',
        expectedBranch: 'feature/a',
      },
      isIssuesAgentClone: false,
    })
    const issuesClone = createMockClone({
      clone: {
        ...createMockClone().clone,
        path: '/repos/.clones/issues+b',
        expectedBranch: 'issues/b',
      },
      isIssuesAgentClone: true,
    })

    mockEnrichedClonesReturn = {
      ...mockEnrichedClonesReturn,
      data: [featureClone, issuesClone],
    }

    render(<ClonesTab projectId="project-1" />)

    expect(screen.getByText('Feature Clones')).toBeInTheDocument()
    expect(screen.getByText('Issues Agent Clones')).toBeInTheDocument()
    expect(screen.getByText('feature/a')).toBeInTheDocument()
    expect(screen.getByText('issues/b')).toBeInTheDocument()
  })

  it('shows Delete All button with correct count', () => {
    const deletableClone1 = createMockClone({
      clone: {
        ...createMockClone().clone,
        path: '/repos/.clones/stale1',
      },
      isDeletable: true,
    })
    const deletableClone2 = createMockClone({
      clone: {
        ...createMockClone().clone,
        path: '/repos/.clones/stale2',
      },
      isDeletable: true,
    })
    const activeClone = createMockClone({
      clone: {
        ...createMockClone().clone,
        path: '/repos/.clones/active',
      },
      isDeletable: false,
    })

    mockEnrichedClonesReturn = {
      ...mockEnrichedClonesReturn,
      data: [deletableClone1, deletableClone2, activeClone],
    }

    render(<ClonesTab projectId="project-1" />)

    expect(screen.getByRole('button', { name: /Delete All Stale \(2\)/ })).toBeInTheDocument()
  })

  it('hides Delete All button when no deletable clones', () => {
    const activeClone = createMockClone({ isDeletable: false })

    mockEnrichedClonesReturn = {
      ...mockEnrichedClonesReturn,
      data: [activeClone],
    }

    render(<ClonesTab projectId="project-1" />)

    expect(screen.queryByRole('button', { name: /Delete All Stale/ })).not.toBeInTheDocument()
  })

  it('bulk deletes all stale clones on confirmation', async () => {
    const deletable1 = createMockClone({
      clone: {
        ...createMockClone().clone,
        path: '/repos/.clones/stale1',
      },
      isDeletable: true,
    })
    const deletable2 = createMockClone({
      clone: {
        ...createMockClone().clone,
        path: '/repos/.clones/stale2',
      },
      isDeletable: true,
    })

    mockEnrichedClonesReturn = {
      ...mockEnrichedClonesReturn,
      data: [deletable1, deletable2],
    }

    render(<ClonesTab projectId="project-1" />)

    // Click Delete All button to open dialog
    fireEvent.click(screen.getByRole('button', { name: /Delete All Stale \(2\)/ }))

    // Confirm in the dialog
    await waitFor(() => {
      expect(screen.getByText('Delete All Stale Clones')).toBeInTheDocument()
    })
    fireEvent.click(screen.getByRole('button', { name: 'Delete All' }))

    await waitFor(() => {
      expect(mockBulkDeleteMutateAsync).toHaveBeenCalledWith({
        projectId: 'project-1',
        clonePaths: ['/repos/.clones/stale1', '/repos/.clones/stale2'],
      })
    })
  })

  it('deletes single clone on card action', async () => {
    const clone = createMockClone({
      clone: {
        ...createMockClone().clone,
        path: '/repos/.clones/feature+single',
      },
    })

    mockEnrichedClonesReturn = {
      ...mockEnrichedClonesReturn,
      data: [clone],
    }

    render(<ClonesTab projectId="project-1" />)

    // Click the delete button on the card
    fireEvent.click(screen.getByTestId('delete-/repos/.clones/feature+single'))

    await waitFor(() => {
      expect(mockDeleteCloneMutateAsync).toHaveBeenCalledWith({
        projectId: 'project-1',
        clonePath: '/repos/.clones/feature+single',
      })
    })
  })
})
