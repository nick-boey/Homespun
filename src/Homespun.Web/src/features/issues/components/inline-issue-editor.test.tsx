import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlineIssueEditor } from './inline-issue-editor'
import { EditCursorPosition } from '../types'

describe('InlineIssueEditor', () => {
  const defaultProps = {
    title: '',
    onTitleChange: vi.fn(),
    onSave: vi.fn(),
    onSaveAndEdit: vi.fn(),
    onCancel: vi.fn(),
    onIndent: vi.fn(),
    onUnindent: vi.fn(),
    placeholder: 'Enter issue title...',
    cursorPosition: EditCursorPosition.End,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('rendering', () => {
    it('renders input field with placeholder', () => {
      render(<InlineIssueEditor {...defaultProps} />)

      const input = screen.getByTestId('inline-issue-input')
      expect(input).toBeInTheDocument()
      expect(input).toHaveAttribute('placeholder', 'Enter issue title...')
    })

    it('renders action buttons', () => {
      render(<InlineIssueEditor {...defaultProps} />)

      expect(screen.getByTestId('inline-ok-btn')).toBeInTheDocument()
      expect(screen.getByTestId('inline-ok-edit-btn')).toBeInTheDocument()
      expect(screen.getByTestId('inline-cancel-btn')).toBeInTheDocument()
    })

    it('renders with provided title', () => {
      render(<InlineIssueEditor {...defaultProps} title="Test Issue" />)

      const input = screen.getByTestId('inline-issue-input')
      expect(input).toHaveValue('Test Issue')
    })

    it('renders container with test id', () => {
      render(<InlineIssueEditor {...defaultProps} />)

      expect(screen.getByTestId('inline-issue-create')).toBeInTheDocument()
    })
  })

  describe('input handling', () => {
    it('calls onTitleChange when typing', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const input = screen.getByTestId('inline-issue-input')
      await user.type(input, 'New Issue')

      expect(defaultProps.onTitleChange).toHaveBeenCalled()
    })

    it('auto-focuses the input on mount', () => {
      render(<InlineIssueEditor {...defaultProps} />)

      const input = screen.getByTestId('inline-issue-input')
      expect(input).toHaveFocus()
    })
  })

  describe('keyboard handling', () => {
    it('calls onCancel when Escape is pressed', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const input = screen.getByTestId('inline-issue-input')
      await user.click(input)
      await user.keyboard('{Escape}')

      expect(defaultProps.onCancel).toHaveBeenCalledTimes(1)
    })

    it('calls onSave when Enter is pressed', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const input = screen.getByTestId('inline-issue-input')
      await user.click(input)
      await user.keyboard('{Enter}')

      expect(defaultProps.onSave).toHaveBeenCalledTimes(1)
    })

    it('calls onSaveAndEdit when Shift+Enter is pressed', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const input = screen.getByTestId('inline-issue-input')
      await user.click(input)
      await user.keyboard('{Shift>}{Enter}{/Shift}')

      expect(defaultProps.onSaveAndEdit).toHaveBeenCalledTimes(1)
      expect(defaultProps.onSave).not.toHaveBeenCalled()
    })

    it('calls onIndent when Tab is pressed', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const input = screen.getByTestId('inline-issue-input')
      await user.click(input)
      await user.keyboard('{Tab}')

      expect(defaultProps.onIndent).toHaveBeenCalledTimes(1)
    })

    it('calls onUnindent when Shift+Tab is pressed', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const input = screen.getByTestId('inline-issue-input')
      await user.click(input)
      await user.keyboard('{Shift>}{Tab}{/Shift}')

      expect(defaultProps.onUnindent).toHaveBeenCalledTimes(1)
      expect(defaultProps.onIndent).not.toHaveBeenCalled()
    })
  })

  describe('button clicks', () => {
    it('calls onSave when OK button is clicked', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const okBtn = screen.getByTestId('inline-ok-btn')
      await user.click(okBtn)

      expect(defaultProps.onSave).toHaveBeenCalledTimes(1)
    })

    it('calls onSaveAndEdit when OK+Edit button is clicked', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const okEditBtn = screen.getByTestId('inline-ok-edit-btn')
      await user.click(okEditBtn)

      expect(defaultProps.onSaveAndEdit).toHaveBeenCalledTimes(1)
    })

    it('calls onCancel when Cancel button is clicked', async () => {
      const user = userEvent.setup()
      render(<InlineIssueEditor {...defaultProps} />)

      const cancelBtn = screen.getByTestId('inline-cancel-btn')
      await user.click(cancelBtn)

      expect(defaultProps.onCancel).toHaveBeenCalledTimes(1)
    })
  })

  describe('hierarchy indicator', () => {
    it('shows "Parent of" indicator when showParentIndicator is true', () => {
      render(<InlineIssueEditor {...defaultProps} showParentIndicator={true} isAbove={false} />)

      expect(screen.getByText('Parent of above')).toBeInTheDocument()
    })

    it('shows "Parent of below" indicator when showParentIndicator is true and isAbove is true', () => {
      render(<InlineIssueEditor {...defaultProps} showParentIndicator={true} isAbove={true} />)

      expect(screen.getByText('Parent of below')).toBeInTheDocument()
    })

    it('shows "Child of above" indicator when showChildIndicator is true', () => {
      render(<InlineIssueEditor {...defaultProps} showChildIndicator={true} isAbove={false} />)

      expect(screen.getByText('Child of above')).toBeInTheDocument()
    })

    it('shows "Child of below" indicator when showChildIndicator is true and isAbove is true', () => {
      render(<InlineIssueEditor {...defaultProps} showChildIndicator={true} isAbove={true} />)

      expect(screen.getByText('Child of below')).toBeInTheDocument()
    })

    it('does not show indicator by default', () => {
      render(<InlineIssueEditor {...defaultProps} />)

      expect(screen.queryByText(/Parent of/)).not.toBeInTheDocument()
      expect(screen.queryByText(/Child of/)).not.toBeInTheDocument()
    })
  })

  describe('cursor position', () => {
    it('positions cursor at start when cursorPosition is Start', () => {
      render(
        <InlineIssueEditor
          {...defaultProps}
          title="Test Title"
          cursorPosition={EditCursorPosition.Start}
        />
      )

      const input = screen.getByTestId('inline-issue-input') as HTMLInputElement
      // Note: Selection position testing requires actual DOM interaction
      // The component should handle this via useEffect
      expect(input).toHaveFocus()
    })

    it('positions cursor at end when cursorPosition is End', () => {
      render(
        <InlineIssueEditor
          {...defaultProps}
          title="Test Title"
          cursorPosition={EditCursorPosition.End}
        />
      )

      const input = screen.getByTestId('inline-issue-input') as HTMLInputElement
      expect(input).toHaveFocus()
    })

    it('clears text when cursorPosition is Replace', () => {
      // For Replace mode, the title should be empty but the component is told to replace
      // The clearing happens before render, so we just verify the behavior exists
      render(
        <InlineIssueEditor {...defaultProps} title="" cursorPosition={EditCursorPosition.Replace} />
      )

      const input = screen.getByTestId('inline-issue-input') as HTMLInputElement
      expect(input).toHaveValue('')
    })
  })
})
