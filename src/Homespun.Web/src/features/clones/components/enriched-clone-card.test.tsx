import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import React from 'react'
import { EnrichedCloneCard } from './enriched-clone-card'
import type { EnrichedCloneInfo } from '@/api/generated/types.gen'
import { PullRequestStatus } from '@/api/generated/types.gen'

// Mock TanStack Router
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
    search,
    ...props
  }: {
    children: React.ReactNode
    to: string
    params?: Record<string, string>
    search?: Record<string, string>
    [key: string]: unknown
  }) => {
    let href = to
    if (params) {
      Object.entries(params).forEach(([key, value]) => {
        href = href.replace(`$${key}`, value)
      })
    }
    if (search) {
      const searchParams = new URLSearchParams(
        Object.entries(search).map(([k, v]) => [k, String(v)])
      )
      href = `${href}?${searchParams.toString()}`
    }
    return React.createElement('a', { href, ...props }, children)
  },
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

describe('EnrichedCloneCard', () => {
  it('renders clone branch name and folder', () => {
    const clone = createMockClone()
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.getByText('feature/test-branch')).toBeInTheDocument()
    // Folder name is different from branch name, so it should show in parentheses
    expect(screen.getByText('(feature+test-branch)')).toBeInTheDocument()
    expect(screen.getByText('abc1234')).toBeInTheDocument()
  })

  it('renders linked issue with status badge', () => {
    const clone = createMockClone({
      linkedIssueId: 'issue-123',
      linkedIssue: {
        id: 'issue-123',
        title: 'Test Issue Title',
        status: 'progress',
        type: 'task',
      },
    })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.getByText('Issue:')).toBeInTheDocument()
    expect(screen.getByText('Test Issue Title')).toBeInTheDocument()
    expect(screen.getByText('progress')).toBeInTheDocument()

    // Verify link
    const issueLink = screen.getByRole('link', { name: 'Test Issue Title' })
    expect(issueLink).toHaveAttribute('href', '/projects/project-1/issues?selected=issue-123')
  })

  it('renders linked PR with external link', () => {
    const clone = createMockClone({
      linkedPr: {
        number: 42,
        title: 'Add new feature',
        status: PullRequestStatus.IN_PROGRESS,
        htmlUrl: 'https://github.com/test/repo/pull/42',
      },
    })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.getByText('PR:')).toBeInTheDocument()
    expect(screen.getByText(/#42: Add new feature/)).toBeInTheDocument()

    // Verify external link
    const prLink = screen.getByRole('link', { name: /#42: Add new feature/ })
    expect(prLink).toHaveAttribute('href', 'https://github.com/test/repo/pull/42')
    expect(prLink).toHaveAttribute('target', '_blank')
    expect(prLink).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('shows delete button when isDeletable is true', () => {
    const clone = createMockClone({ isDeletable: true })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.getByRole('button')).toBeInTheDocument()
  })

  it('hides delete button when isDeletable is false', () => {
    const clone = createMockClone({ isDeletable: false })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows deletion reason when provided', () => {
    const clone = createMockClone({
      isDeletable: true,
      deletionReason: 'Issue is complete',
    })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.getByText('Issue is complete')).toBeInTheDocument()
  })

  it('shows warning when issue not found', () => {
    const clone = createMockClone({
      linkedIssueId: 'deleted-issue-456',
      linkedIssue: undefined,
    })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.getByText(/Issue deleted-issue-456 not found/)).toBeInTheDocument()
  })

  it('opens confirmation dialog on delete click', async () => {
    const clone = createMockClone({ isDeletable: true })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    const deleteButton = screen.getByRole('button')
    fireEvent.click(deleteButton)

    // Dialog should appear
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    expect(screen.getByText('Delete Clone')).toBeInTheDocument()
    expect(
      screen.getByText(/Are you sure you want to delete the clone for branch/)
    ).toBeInTheDocument()
  })

  it('calls onDelete when confirmed', async () => {
    const onDelete = vi.fn()
    const clone = createMockClone({ isDeletable: true })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={onDelete} />)

    // Open dialog
    const deleteButton = screen.getByRole('button')
    fireEvent.click(deleteButton)

    // Click confirm
    const confirmButton = screen.getByRole('button', { name: 'Delete' })
    fireEvent.click(confirmButton)

    expect(onDelete).toHaveBeenCalledTimes(1)
  })

  it('does not show folder when it matches branch name', () => {
    const clone = createMockClone({
      clone: {
        path: '/repos/.clones/main',
        workdirPath: '/repos/.clones/main/workdir',
        branch: 'refs/heads/main',
        headCommit: 'def456',
        isBare: false,
        isDetached: false,
        expectedBranch: 'main',
        folderName: 'main',
      },
    })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.getByText('main')).toBeInTheDocument()
    // Should NOT show folder name in parentheses when it matches branch
    expect(screen.queryByText('(main)')).not.toBeInTheDocument()
  })

  it('disables delete button when isDeleting is true', () => {
    const clone = createMockClone({ isDeletable: true })
    render(
      <EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} isDeleting={true} />
    )

    const deleteButton = screen.getByRole('button')
    expect(deleteButton).toBeDisabled()
  })

  it('applies dashed border when isDeletable is true', () => {
    const clone = createMockClone({ isDeletable: true })
    const { container } = render(
      <EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />
    )

    const card = container.querySelector('[data-slot="card"]')
    expect(card).toHaveClass('border-dashed')
  })

  it('renders PR status badge with correct variant', () => {
    const clone = createMockClone({
      linkedPr: {
        number: 1,
        title: 'PR Title',
        status: PullRequestStatus.MERGED,
        htmlUrl: 'https://github.com/test/repo/pull/1',
      },
    })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    const badge = screen.getByText(PullRequestStatus.MERGED)
    expect(badge).toBeInTheDocument()
  })

  it('uses branch from refs/heads when expectedBranch is not available', () => {
    const clone = createMockClone({
      clone: {
        path: '/repos/.clones/feature+fallback',
        workdirPath: '/repos/.clones/feature+fallback/workdir',
        branch: 'refs/heads/feature/fallback-branch',
        headCommit: 'xyz789',
        isBare: false,
        isDetached: false,
        expectedBranch: undefined,
        folderName: 'feature+fallback',
      },
    })
    render(<EnrichedCloneCard clone={clone} projectId="project-1" onDelete={vi.fn()} />)

    expect(screen.getByText('feature/fallback-branch')).toBeInTheDocument()
  })
})
