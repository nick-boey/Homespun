import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SessionBranchTab } from './session-branch-tab'
import { useSessionBranchInfo } from '@/features/sessions/hooks'
import { createMockSession } from '@/test/test-utils'

vi.mock('@/features/sessions/hooks', () => ({
  useSessionBranchInfo: vi.fn(),
}))

describe('SessionBranchTab', () => {
  const mockCloneSession = createMockSession({
    entityId: 'clone:feature-branch',
    workingDirectory: '/path/to/clone/workdir',
  })

  const mockNonCloneSession = createMockSession({
    entityId: 'issue-123',
    workingDirectory: '/path/to/project',
  })

  it('shows empty state for non-clone sessions', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: false,
      isCloneSession: false,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    render(<SessionBranchTab session={mockNonCloneSession} />)

    expect(screen.getByText('Branch info not available')).toBeInTheDocument()
    expect(screen.getByText(/only available for clone sessions/i)).toBeInTheDocument()
  })

  it('shows loading state', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
      isCloneSession: true,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    const { container } = render(<SessionBranchTab session={mockCloneSession} />)

    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0)
  })

  it('shows error state', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      isCloneSession: true,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    render(<SessionBranchTab session={mockCloneSession} />)

    expect(screen.getByText('Failed to load branch info')).toBeInTheDocument()
  })

  it('shows branch info with all fields', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: {
        branchName: 'feature/awesome-feature',
        commitSha: 'abc1234',
        commitMessage: 'Add awesome feature implementation',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 2,
        behindCount: 1,
        hasUncommittedChanges: true,
      },
      isLoading: false,
      isError: false,
      isCloneSession: true,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    render(<SessionBranchTab session={mockCloneSession} />)

    // Branch name
    expect(screen.getByText('feature/awesome-feature')).toBeInTheDocument()

    // Commit SHA
    expect(screen.getByText('abc1234')).toBeInTheDocument()

    // Commit message
    expect(screen.getByText('Add awesome feature implementation')).toBeInTheDocument()

    // Ahead/behind badges
    expect(screen.getByText('2 ahead')).toBeInTheDocument()
    expect(screen.getByText('1 behind')).toBeInTheDocument()

    // Uncommitted changes warning
    expect(screen.getByText(/uncommitted changes/i)).toBeInTheDocument()
  })

  it('hides ahead badge when aheadCount is 0', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: {
        branchName: 'main',
        commitSha: 'def5678',
        commitMessage: 'Latest commit',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 0,
        behindCount: 3,
        hasUncommittedChanges: false,
      },
      isLoading: false,
      isError: false,
      isCloneSession: true,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    render(<SessionBranchTab session={mockCloneSession} />)

    expect(screen.queryByText(/ahead/i)).not.toBeInTheDocument()
    expect(screen.getByText('3 behind')).toBeInTheDocument()
  })

  it('hides behind badge when behindCount is 0', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: {
        branchName: 'main',
        commitSha: 'def5678',
        commitMessage: 'Latest commit',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 5,
        behindCount: 0,
        hasUncommittedChanges: false,
      },
      isLoading: false,
      isError: false,
      isCloneSession: true,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    render(<SessionBranchTab session={mockCloneSession} />)

    expect(screen.getByText('5 ahead')).toBeInTheDocument()
    expect(screen.queryByText(/behind/i)).not.toBeInTheDocument()
  })

  it('hides uncommitted changes warning when hasUncommittedChanges is false', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: {
        branchName: 'main',
        commitSha: 'def5678',
        commitMessage: 'Latest commit',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 0,
        behindCount: 0,
        hasUncommittedChanges: false,
      },
      isLoading: false,
      isError: false,
      isCloneSession: true,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    render(<SessionBranchTab session={mockCloneSession} />)

    expect(screen.queryByText(/uncommitted changes/i)).not.toBeInTheDocument()
  })

  it('handles detached HEAD (null branch name)', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: {
        branchName: null,
        commitSha: 'ghi9012',
        commitMessage: 'Detached commit',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 0,
        behindCount: 0,
        hasUncommittedChanges: false,
      },
      isLoading: false,
      isError: false,
      isCloneSession: true,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    render(<SessionBranchTab session={mockCloneSession} />)

    // Should show detached HEAD indicator
    expect(screen.getByText(/detached HEAD/i)).toBeInTheDocument()
    expect(screen.getByText('ghi9012')).toBeInTheDocument()
  })

  it('shows up to date message when not ahead or behind', () => {
    vi.mocked(useSessionBranchInfo).mockReturnValue({
      data: {
        branchName: 'main',
        commitSha: 'def5678',
        commitMessage: 'Latest commit',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 0,
        behindCount: 0,
        hasUncommittedChanges: false,
      },
      isLoading: false,
      isError: false,
      isCloneSession: true,
    } as unknown as ReturnType<typeof useSessionBranchInfo>)

    render(<SessionBranchTab session={mockCloneSession} />)

    expect(screen.getByText(/up to date/i)).toBeInTheDocument()
  })
})
