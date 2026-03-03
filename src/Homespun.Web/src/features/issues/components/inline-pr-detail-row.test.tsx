import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlinePrDetailRow } from './inline-pr-detail-row'
import type { TaskGraphPrRenderLine } from '../services'

// Helper to create a mock PR render line
function createPrRenderLine(overrides: Partial<TaskGraphPrRenderLine> = {}): TaskGraphPrRenderLine {
  return {
    type: 'pr',
    prNumber: 123,
    title: 'Add new feature',
    url: 'https://github.com/test/repo/pull/123',
    isMerged: true,
    hasDescription: true,
    agentStatus: null,
    drawTopLine: false,
    drawBottomLine: false,
    ...overrides,
  }
}

describe('InlinePrDetailRow', () => {
  const defaultProps = {
    line: createPrRenderLine(),
    maxLanes: 3,
    description: 'This is the PR description with **markdown** support.',
    commits: [
      { sha: 'abc1234', message: 'Initial commit' },
      { sha: 'def5678', message: 'Add tests' },
    ],
    relatedIssues: [
      { id: 'issue-1', title: 'Related issue 1' },
      { id: 'issue-2', title: 'Related issue 2' },
    ],
    onClose: vi.fn(),
    onViewIssue: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('rendering', () => {
    it('renders PR number', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByText('#123')).toBeInTheDocument()
    })

    it('renders PR title', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByText('Add new feature')).toBeInTheDocument()
    })

    it('renders merged badge for merged PR', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByText('Merged')).toBeInTheDocument()
    })

    it('renders closed badge for closed PR', () => {
      const line = createPrRenderLine({ isMerged: false })
      render(<InlinePrDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Closed')).toBeInTheDocument()
    })
  })

  describe('description', () => {
    it('renders PR description', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      // Markdown renders bold text
      expect(screen.getByText(/PR description/i)).toBeInTheDocument()
    })

    it('shows no description message when description is null', () => {
      render(<InlinePrDetailRow {...defaultProps} description={null} />)
      expect(screen.getByText(/no description/i)).toBeInTheDocument()
    })
  })

  describe('commits list', () => {
    it('renders commits section title', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByText(/commits/i)).toBeInTheDocument()
    })

    it('renders commit messages', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByText('Initial commit')).toBeInTheDocument()
      expect(screen.getByText('Add tests')).toBeInTheDocument()
    })

    it('renders commit short SHAs', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByText('abc1234')).toBeInTheDocument()
      expect(screen.getByText('def5678')).toBeInTheDocument()
    })

    it('shows no commits message when commits array is empty', () => {
      render(<InlinePrDetailRow {...defaultProps} commits={[]} />)
      expect(screen.getByText(/no commits/i)).toBeInTheDocument()
    })
  })

  describe('related issues', () => {
    it('renders related issues section title', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByText(/related issues/i)).toBeInTheDocument()
    })

    it('renders related issue titles', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByText('Related issue 1')).toBeInTheDocument()
      expect(screen.getByText('Related issue 2')).toBeInTheDocument()
    })

    it('calls onViewIssue when issue is clicked', async () => {
      const user = userEvent.setup()
      const onViewIssue = vi.fn()
      render(<InlinePrDetailRow {...defaultProps} onViewIssue={onViewIssue} />)

      await user.click(screen.getByText('Related issue 1'))
      expect(onViewIssue).toHaveBeenCalledWith('issue-1')
    })

    it('does not render related issues section when array is empty', () => {
      render(<InlinePrDetailRow {...defaultProps} relatedIssues={[]} />)
      expect(screen.queryByText(/related issues/i)).not.toBeInTheDocument()
    })
  })

  describe('view PR link', () => {
    it('renders link to view PR on GitHub', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      const link = screen.getByRole('link', { name: /view on github/i })
      expect(link).toHaveAttribute('href', 'https://github.com/test/repo/pull/123')
    })

    it('opens link in new tab', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      const link = screen.getByRole('link', { name: /view on github/i })
      expect(link).toHaveAttribute('target', '_blank')
    })
  })

  describe('close behavior', () => {
    it('renders close button', () => {
      render(<InlinePrDetailRow {...defaultProps} />)
      expect(screen.getByRole('button', { name: /close/i })).toBeInTheDocument()
    })

    it('calls onClose when close button is clicked', async () => {
      const user = userEvent.setup()
      const onClose = vi.fn()
      render(<InlinePrDetailRow {...defaultProps} onClose={onClose} />)

      await user.click(screen.getByRole('button', { name: /close/i }))
      expect(onClose).toHaveBeenCalled()
    })
  })

  describe('styling', () => {
    it('applies correct left margin based on maxLanes', () => {
      const { container } = render(<InlinePrDetailRow {...defaultProps} maxLanes={3} />)
      const detailRow = container.firstChild as HTMLElement
      // SVG width = 24 * max(maxLanes, 1) + 12 = 24 * 3 + 12 = 84
      expect(detailRow).toHaveStyle({ marginLeft: '84px' })
    })

    it('has expand animation class', () => {
      const { container } = render(<InlinePrDetailRow {...defaultProps} />)
      const detailRow = container.firstChild as HTMLElement
      expect(detailRow).toHaveClass('animate-expand')
    })
  })
})
