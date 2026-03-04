import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlineIssueDetailRow } from './inline-issue-detail-row'
import type { TaskGraphIssueRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'

// Helper to create a mock render line
function createRenderLine(
  overrides: Partial<TaskGraphIssueRenderLine> = {}
): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    issueId: 'abc123',
    title: 'Test Issue',
    description: 'This is a test description',
    branchName: 'feature/test-branch+abc123',
    lane: 0,
    marker: TaskGraphMarkerType.Open,
    parentLane: null,
    isFirstChild: false,
    isSeriesChild: false,
    drawTopLine: false,
    drawBottomLine: false,
    seriesConnectorFromLane: null,
    issueType: 0, // Task
    status: 0, // Open
    hasDescription: true,
    linkedPr: null,
    agentStatus: null,
    drawLane0Connector: false,
    isLastLane0Connector: false,
    drawLane0PassThrough: false,
    lane0Color: null,
    hasHiddenParent: false,
    hiddenParentIsSeriesMode: false,
    ...overrides,
  }
}

describe('InlineIssueDetailRow', () => {
  const defaultProps = {
    line: createRenderLine(),
    maxLanes: 3,
    onEdit: vi.fn(),
    onRunAgent: vi.fn(),
    onClose: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('rendering', () => {
    it('renders issue ID', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('abc123')).toBeInTheDocument()
    })

    it('renders type badge for task', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('Task')).toBeInTheDocument()
    })

    it('renders type badge for bug', () => {
      const line = createRenderLine({ issueType: 1 })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Bug')).toBeInTheDocument()
    })

    it('renders type badge for chore', () => {
      const line = createRenderLine({ issueType: 2 })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Chore')).toBeInTheDocument()
    })

    it('renders type badge for feature', () => {
      const line = createRenderLine({ issueType: 3 })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Feature')).toBeInTheDocument()
    })

    it('renders type badge for idea', () => {
      const line = createRenderLine({ issueType: 4 })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Idea')).toBeInTheDocument()
    })

    it('renders status badge for open', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('Open')).toBeInTheDocument()
    })

    it('renders status badge for in progress', () => {
      const line = createRenderLine({ status: 1 })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('In Progress')).toBeInTheDocument()
    })

    it('renders status badge for in review', () => {
      const line = createRenderLine({ status: 2 })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Review')).toBeInTheDocument()
    })

    it('renders status badge for complete', () => {
      const line = createRenderLine({ status: 3 })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Complete')).toBeInTheDocument()
    })
  })

  describe('branch name', () => {
    it('renders branch name when available', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('feature/test-branch+abc123')).toBeInTheDocument()
    })

    it('renders copy button for branch name', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByRole('button', { name: /copy branch/i })).toBeInTheDocument()
    })

    it('does not render branch section when branch name is null', () => {
      const line = createRenderLine({ branchName: null })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.queryByRole('button', { name: /copy branch/i })).not.toBeInTheDocument()
    })

    it('copies branch name to clipboard when copy button is clicked', async () => {
      const user = userEvent.setup()
      const writeTextMock = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', {
        value: { writeText: writeTextMock },
        writable: true,
        configurable: true,
      })

      render(<InlineIssueDetailRow {...defaultProps} />)
      await user.click(screen.getByRole('button', { name: /copy branch/i }))

      expect(writeTextMock).toHaveBeenCalledWith('feature/test-branch+abc123')
    })
  })

  describe('description', () => {
    it('renders description text', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('This is a test description')).toBeInTheDocument()
    })

    it('shows no description message when description is null', () => {
      const line = createRenderLine({ description: null })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText(/no description/i)).toBeInTheDocument()
    })

    it('renders markdown in description', () => {
      const line = createRenderLine({ description: '**Bold text** and `code`' })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      // Check that markdown elements are rendered (bold text becomes <strong>)
      expect(screen.getByText('Bold text')).toBeInTheDocument()
    })
  })

  describe('linked PR', () => {
    it('renders linked PR badge when available', () => {
      const line = createRenderLine({
        linkedPr: { number: 123, url: 'https://github.com/test/pr/123', status: 'open' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('#123')).toBeInTheDocument()
    })

    it('shows open status badge for open PR', () => {
      const line = createRenderLine({
        linkedPr: { number: 123, url: 'https://github.com/test/pr/123', status: 'open' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByTestId('pr-status-badge')).toHaveTextContent(/open/i)
    })

    it('shows merged status badge for merged PR', () => {
      const line = createRenderLine({
        linkedPr: { number: 123, url: 'https://github.com/test/pr/123', status: 'merged' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByTestId('pr-status-badge')).toHaveTextContent(/merged/i)
    })

    it('links to PR URL when clicked', () => {
      const line = createRenderLine({
        linkedPr: { number: 123, url: 'https://github.com/test/pr/123', status: 'open' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      const link = screen.getByRole('link', { name: /#123/i })
      expect(link).toHaveAttribute('href', 'https://github.com/test/pr/123')
    })
  })

  describe('agent status', () => {
    it('shows active agent indicator when agent is running', () => {
      const line = createRenderLine({
        agentStatus: { isActive: true, status: 'running', sessionId: 'session-123' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByTestId('agent-status-indicator')).toBeInTheDocument()
      expect(screen.getByText(/running/i)).toBeInTheDocument()
    })

    it('shows link to session when agent is active', () => {
      const line = createRenderLine({
        agentStatus: { isActive: true, status: 'running', sessionId: 'session-123' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByRole('link', { name: /view session/i })).toBeInTheDocument()
    })

    it('does not show agent section when agent is inactive', () => {
      const line = createRenderLine({
        agentStatus: { isActive: false, status: 'stopped', sessionId: null },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.queryByTestId('agent-status-indicator')).not.toBeInTheDocument()
    })
  })

  describe('actions', () => {
    it('renders edit button', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument()
    })

    it('renders run agent button', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByRole('button', { name: /run agent/i })).toBeInTheDocument()
    })

    it('calls onEdit when edit button is clicked', async () => {
      const user = userEvent.setup()
      const onEdit = vi.fn()
      render(<InlineIssueDetailRow {...defaultProps} onEdit={onEdit} />)

      await user.click(screen.getByRole('button', { name: /edit/i }))
      expect(onEdit).toHaveBeenCalledWith('abc123')
    })

    it('calls onRunAgent when run agent button is clicked', async () => {
      const user = userEvent.setup()
      const onRunAgent = vi.fn()
      render(<InlineIssueDetailRow {...defaultProps} onRunAgent={onRunAgent} />)

      await user.click(screen.getByRole('button', { name: /run agent/i }))
      expect(onRunAgent).toHaveBeenCalledWith('abc123')
    })
  })

  describe('close behavior', () => {
    it('calls onClose when close button is clicked', async () => {
      const user = userEvent.setup()
      const onClose = vi.fn()
      render(<InlineIssueDetailRow {...defaultProps} onClose={onClose} />)

      await user.click(screen.getByRole('button', { name: /close/i }))
      expect(onClose).toHaveBeenCalled()
    })
  })

  describe('styling', () => {
    it('applies correct left margin based on maxLanes', () => {
      const { container } = render(<InlineIssueDetailRow {...defaultProps} maxLanes={3} />)
      const detailRow = container.firstChild as HTMLElement
      // SVG width = 24 * max(maxLanes, 1) + 12 = 24 * 3 + 12 = 84
      expect(detailRow).toHaveStyle({ marginLeft: '84px' })
    })

    it('has expand animation class', () => {
      const { container } = render(<InlineIssueDetailRow {...defaultProps} />)
      const detailRow = container.firstChild as HTMLElement
      expect(detailRow).toHaveClass('animate-expand')
    })
  })
})
