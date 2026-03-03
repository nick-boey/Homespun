import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { IssueRowActions } from './issue-row-actions'

describe('IssueRowActions', () => {
  const defaultProps = {
    issueId: 'abc123',
    onEdit: vi.fn(),
    onRunAgent: vi.fn(),
    onExpand: vi.fn(),
    isExpanded: false,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('rendering', () => {
    it('renders edit button', () => {
      render(<IssueRowActions {...defaultProps} />)
      expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument()
    })

    it('renders run agent button', () => {
      render(<IssueRowActions {...defaultProps} />)
      expect(screen.getByRole('button', { name: /run agent/i })).toBeInTheDocument()
    })

    it('renders expand button when not expanded', () => {
      render(<IssueRowActions {...defaultProps} isExpanded={false} />)
      expect(screen.getByRole('button', { name: /expand/i })).toBeInTheDocument()
    })

    it('renders collapse button when expanded', () => {
      render(<IssueRowActions {...defaultProps} isExpanded={true} />)
      expect(screen.getByRole('button', { name: /collapse/i })).toBeInTheDocument()
    })
  })

  describe('interactions', () => {
    it('calls onEdit when edit button is clicked', async () => {
      const user = userEvent.setup()
      const onEdit = vi.fn()
      render(<IssueRowActions {...defaultProps} onEdit={onEdit} />)

      await user.click(screen.getByRole('button', { name: /edit/i }))
      expect(onEdit).toHaveBeenCalledWith('abc123')
    })

    it('calls onRunAgent when run agent button is clicked', async () => {
      const user = userEvent.setup()
      const onRunAgent = vi.fn()
      render(<IssueRowActions {...defaultProps} onRunAgent={onRunAgent} />)

      await user.click(screen.getByRole('button', { name: /run agent/i }))
      expect(onRunAgent).toHaveBeenCalledWith('abc123')
    })

    it('calls onExpand when expand button is clicked', async () => {
      const user = userEvent.setup()
      const onExpand = vi.fn()
      render(<IssueRowActions {...defaultProps} onExpand={onExpand} />)

      await user.click(screen.getByRole('button', { name: /expand/i }))
      expect(onExpand).toHaveBeenCalled()
    })

    it('stops event propagation on button clicks', async () => {
      const user = userEvent.setup()
      const onContainerClick = vi.fn()
      const onEdit = vi.fn()

      render(
        <div onClick={onContainerClick}>
          <IssueRowActions {...defaultProps} onEdit={onEdit} />
        </div>
      )

      await user.click(screen.getByRole('button', { name: /edit/i }))
      expect(onEdit).toHaveBeenCalled()
      expect(onContainerClick).not.toHaveBeenCalled()
    })
  })

  describe('visibility', () => {
    it('has opacity-0 class by default for hover behavior', () => {
      const { container } = render(<IssueRowActions {...defaultProps} />)
      const actionsContainer = container.firstChild as HTMLElement
      expect(actionsContainer).toHaveClass('opacity-0')
    })

    it('has group-hover:opacity-100 for visibility on hover', () => {
      const { container } = render(<IssueRowActions {...defaultProps} />)
      const actionsContainer = container.firstChild as HTMLElement
      expect(actionsContainer).toHaveClass('group-hover:opacity-100')
    })
  })

  describe('button styling', () => {
    it('uses ghost variant for compact buttons', () => {
      render(<IssueRowActions {...defaultProps} />)
      const buttons = screen.getAllByRole('button')
      buttons.forEach((button) => {
        expect(button).toHaveAttribute('data-variant', 'ghost')
      })
    })
  })
})
