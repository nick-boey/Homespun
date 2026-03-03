import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RemoteBranchRow } from './remote-branch-row'
import type { BranchInfo } from '@/api/generated/types.gen'

const mockBranch: BranchInfo = {
  name: 'refs/heads/feature/remote-branch',
  shortName: 'feature/remote-branch',
  commitSha: 'def456abc789',
  lastCommitMessage: 'Remote commit message',
  lastCommitDate: new Date(Date.now() - 86400000).toISOString(), // 1 day ago
}

describe('RemoteBranchRow', () => {
  const defaultProps = {
    branch: mockBranch,
    onCreateWorktree: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders branch name', () => {
    render(<RemoteBranchRow {...defaultProps} />)

    expect(screen.getByText('feature/remote-branch')).toBeInTheDocument()
  })

  it('renders commit message', () => {
    render(<RemoteBranchRow {...defaultProps} />)

    expect(screen.getByText('Remote commit message')).toBeInTheDocument()
  })

  it('renders Create Worktree button', () => {
    render(<RemoteBranchRow {...defaultProps} />)

    expect(screen.getByRole('button', { name: /Create Worktree/i })).toBeInTheDocument()
  })

  it('calls onCreateWorktree when button is clicked', async () => {
    const user = userEvent.setup()
    render(<RemoteBranchRow {...defaultProps} />)

    const button = screen.getByRole('button', { name: /Create Worktree/i })
    await user.click(button)

    expect(defaultProps.onCreateWorktree).toHaveBeenCalledTimes(1)
  })

  it('shows creating state when isCreating is true', () => {
    render(<RemoteBranchRow {...defaultProps} isCreating={true} />)

    expect(screen.getByRole('button', { name: /Creating/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Creating/i })).toBeDisabled()
  })

  it('disables button when isCreating is true', () => {
    render(<RemoteBranchRow {...defaultProps} isCreating={true} />)

    expect(screen.getByRole('button')).toBeDisabled()
  })

  it('renders relative time for commit date', () => {
    render(<RemoteBranchRow {...defaultProps} />)

    expect(screen.getByText('1d ago')).toBeInTheDocument()
  })
})
