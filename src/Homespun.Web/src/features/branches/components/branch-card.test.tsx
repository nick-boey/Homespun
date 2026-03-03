import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { BranchCard } from './branch-card'
import type { BranchInfo } from '@/api/generated/types.gen'

const mockBranch: BranchInfo = {
  name: 'refs/heads/feature/test-branch',
  shortName: 'feature/test-branch',
  commitSha: 'abc123def456',
  aheadCount: 2,
  behindCount: 0,
  lastCommitMessage: 'Add new feature',
  lastCommitDate: new Date(Date.now() - 3600000).toISOString(), // 1 hour ago
}

describe('BranchCard', () => {
  const defaultProps = {
    branch: mockBranch,
    projectId: 'project-1',
    onPull: vi.fn(),
    onDelete: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders branch name', () => {
    render(<BranchCard {...defaultProps} />)

    expect(screen.getByText('feature/test-branch')).toBeInTheDocument()
  })

  it('renders commit message', () => {
    render(<BranchCard {...defaultProps} />)

    expect(screen.getByText('Add new feature')).toBeInTheDocument()
  })

  it('renders commit SHA (truncated)', () => {
    render(<BranchCard {...defaultProps} />)

    expect(screen.getByText('abc123d')).toBeInTheDocument()
  })

  it('shows "ahead" status when branch is ahead', () => {
    render(<BranchCard {...defaultProps} />)

    expect(screen.getByText('2 ahead')).toBeInTheDocument()
  })

  it('shows "behind" status when branch is behind', () => {
    const behindBranch: BranchInfo = {
      ...mockBranch,
      aheadCount: 0,
      behindCount: 3,
    }
    render(<BranchCard {...defaultProps} branch={behindBranch} />)

    expect(screen.getByText('3 behind')).toBeInTheDocument()
  })

  it('shows "Up to date" status when branch is even', () => {
    const evenBranch: BranchInfo = {
      ...mockBranch,
      aheadCount: 0,
      behindCount: 0,
    }
    render(<BranchCard {...defaultProps} branch={evenBranch} />)

    expect(screen.getByText('Up to date')).toBeInTheDocument()
  })

  it('shows merged badge when isMerged is true', () => {
    render(<BranchCard {...defaultProps} isMerged={true} />)

    expect(screen.getByText('Merged')).toBeInTheDocument()
  })

  it('calls onPull when pull button is clicked', async () => {
    const user = userEvent.setup()
    render(<BranchCard {...defaultProps} />)

    const pullButton = screen.getByRole('button', { name: 'Pull latest changes' })
    await user.click(pullButton)

    expect(defaultProps.onPull).toHaveBeenCalledTimes(1)
  })

  it('disables pull button when isPulling is true', () => {
    render(<BranchCard {...defaultProps} isPulling={true} />)

    const pullButton = screen.getByRole('button', { name: 'Pull latest changes' })
    expect(pullButton).toBeDisabled()
  })

  it('shows delete confirmation dialog when delete button is clicked', async () => {
    const user = userEvent.setup()
    render(<BranchCard {...defaultProps} />)

    const deleteButton = screen.getByRole('button', { name: 'Delete worktree' })
    await user.click(deleteButton)

    expect(screen.getByText('Delete Worktree')).toBeInTheDocument()
  })

  it('calls onDelete when deletion is confirmed', async () => {
    const user = userEvent.setup()
    render(<BranchCard {...defaultProps} />)

    const deleteButton = screen.getByRole('button', { name: 'Delete worktree' })
    await user.click(deleteButton)

    const confirmButton = screen.getByRole('button', { name: 'Delete' })
    await user.click(confirmButton)

    expect(defaultProps.onDelete).toHaveBeenCalledTimes(1)
  })

  it('does not call onDelete when deletion is cancelled', async () => {
    const user = userEvent.setup()
    render(<BranchCard {...defaultProps} />)

    const deleteButton = screen.getByRole('button', { name: 'Delete worktree' })
    await user.click(deleteButton)

    const cancelButton = screen.getByRole('button', { name: 'Cancel' })
    await user.click(cancelButton)

    expect(defaultProps.onDelete).not.toHaveBeenCalled()
  })

  it('shows warning for unmerged branches in delete dialog', async () => {
    const user = userEvent.setup()
    render(<BranchCard {...defaultProps} isMerged={false} />)

    const deleteButton = screen.getByRole('button', { name: 'Delete worktree' })
    await user.click(deleteButton)

    expect(screen.getByText(/Warning: This branch has not been merged!/)).toBeInTheDocument()
  })

  it('shows safe message for merged branches in delete dialog', async () => {
    const user = userEvent.setup()
    render(<BranchCard {...defaultProps} isMerged={true} />)

    const deleteButton = screen.getByRole('button', { name: 'Delete worktree' })
    await user.click(deleteButton)

    expect(screen.getByText(/This branch has been merged/)).toBeInTheDocument()
  })

  it('renders start agent button when onStartAgent is provided', () => {
    const onStartAgent = vi.fn()
    render(<BranchCard {...defaultProps} onStartAgent={onStartAgent} />)

    expect(screen.getByRole('button', { name: 'Start agent' })).toBeInTheDocument()
  })

  it('calls onStartAgent when start agent button is clicked', async () => {
    const user = userEvent.setup()
    const onStartAgent = vi.fn()
    render(<BranchCard {...defaultProps} onStartAgent={onStartAgent} />)

    const startButton = screen.getByRole('button', { name: 'Start agent' })
    await user.click(startButton)

    expect(onStartAgent).toHaveBeenCalledTimes(1)
  })

  it('does not render start agent button when onStartAgent is not provided', () => {
    render(<BranchCard {...defaultProps} />)

    expect(screen.queryByRole('button', { name: 'Start agent' })).not.toBeInTheDocument()
  })
})
