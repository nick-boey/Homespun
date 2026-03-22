import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { IssueChangeDetailPanel } from './issue-change-detail-panel'
import { ChangeType, IssueType, IssueStatus } from '@/api'
import type { IssueChangeDto } from '@/api'

function createMockChange(overrides: Partial<IssueChangeDto> = {}): IssueChangeDto {
  return {
    issueId: 'abc123',
    title: 'Test Issue',
    changeType: ChangeType.UPDATED,
    fieldChanges: null,
    originalIssue: undefined,
    modifiedIssue: undefined,
    ...overrides,
  }
}

describe('IssueChangeDetailPanel', () => {
  describe('change type badge', () => {
    it('renders Created badge with green color for created issues', () => {
      const change = createMockChange({ changeType: ChangeType.CREATED })

      render(<IssueChangeDetailPanel change={change} />)

      const badge = screen.getByTestId('change-type-badge')
      expect(badge).toHaveTextContent('Created')
      expect(badge).toHaveClass('border-green-500')
    })

    it('renders Updated badge with yellow color for updated issues', () => {
      const change = createMockChange({ changeType: ChangeType.UPDATED })

      render(<IssueChangeDetailPanel change={change} />)

      const badge = screen.getByTestId('change-type-badge')
      expect(badge).toHaveTextContent('Updated')
      expect(badge).toHaveClass('border-yellow-500')
    })

    it('renders Deleted badge with red color for deleted issues', () => {
      const change = createMockChange({ changeType: ChangeType.DELETED })

      render(<IssueChangeDetailPanel change={change} />)

      const badge = screen.getByTestId('change-type-badge')
      expect(badge).toHaveTextContent('Deleted')
      expect(badge).toHaveClass('border-red-500')
    })
  })

  describe('field changes for updated issues', () => {
    it('shows expandable field changes for updated issues', () => {
      const change = createMockChange({
        changeType: ChangeType.UPDATED,
        fieldChanges: [
          { fieldName: 'status', oldValue: 'draft', newValue: 'in-progress' },
          { fieldName: 'title', oldValue: 'Old Title', newValue: 'New Title' },
        ],
      })

      render(<IssueChangeDetailPanel change={change} />)

      expect(screen.getByText('Fields Changed (2)')).toBeInTheDocument()
      expect(screen.getByText('status:')).toBeInTheDocument()
      expect(screen.getByText('draft')).toBeInTheDocument()
      expect(screen.getByText('in-progress')).toBeInTheDocument()
    })

    it('collapses field changes when toggle clicked', () => {
      const change = createMockChange({
        changeType: ChangeType.UPDATED,
        fieldChanges: [{ fieldName: 'status', oldValue: 'draft', newValue: 'done' }],
      })

      render(<IssueChangeDetailPanel change={change} />)

      // Initially expanded
      expect(screen.getByTestId('field-changes-list')).toBeInTheDocument()

      // Click to collapse
      fireEvent.click(screen.getByTestId('field-changes-toggle'))

      // Should be collapsed
      expect(screen.queryByTestId('field-changes-list')).not.toBeInTheDocument()
    })

    it('shows message when no field changes recorded', () => {
      const change = createMockChange({
        changeType: ChangeType.UPDATED,
        fieldChanges: [],
      })

      render(<IssueChangeDetailPanel change={change} />)

      expect(screen.getByText('No field changes recorded')).toBeInTheDocument()
    })
  })

  describe('created issue info', () => {
    it('shows new issue details for created issues', () => {
      const change = createMockChange({
        changeType: ChangeType.CREATED,
        modifiedIssue: {
          id: 'new-123',
          title: 'New Issue Title',
          description: 'A new issue',
          type: IssueType.TASK,
          status: IssueStatus.DRAFT,
          executionMode: undefined,
          parentIssues: null,
          assignedTo: null,
          priority: null,
          workingBranchId: null,
        },
      })

      render(<IssueChangeDetailPanel change={change} />)

      expect(screen.getByText('New Issue Details')).toBeInTheDocument()
      expect(screen.getByText('new-123')).toBeInTheDocument()
      expect(screen.getByText('New Issue Title')).toBeInTheDocument()
    })
  })

  describe('deleted issue info', () => {
    it('shows original issue info for deleted issues', () => {
      const change = createMockChange({
        changeType: ChangeType.DELETED,
        originalIssue: {
          id: 'del-456',
          title: 'Deleted Issue',
          description: 'This was deleted',
          type: IssueType.BUG,
          status: IssueStatus.COMPLETE,
          executionMode: undefined,
          parentIssues: null,
          assignedTo: null,
          priority: null,
          workingBranchId: null,
        },
      })

      render(<IssueChangeDetailPanel change={change} />)

      expect(screen.getByText('Deleted Issue Details')).toBeInTheDocument()
      expect(screen.getByText('del-456')).toBeInTheDocument()
      expect(screen.getByText('Deleted Issue')).toBeInTheDocument()
    })
  })

  describe('close button', () => {
    it('calls onClose when close button clicked', () => {
      const change = createMockChange()
      const onClose = vi.fn()

      render(<IssueChangeDetailPanel change={change} onClose={onClose} />)

      fireEvent.click(screen.getByTestId('close-detail-panel'))

      expect(onClose).toHaveBeenCalledTimes(1)
    })

    it('does not render close button when onClose not provided', () => {
      const change = createMockChange()

      render(<IssueChangeDetailPanel change={change} />)

      expect(screen.queryByTestId('close-detail-panel')).not.toBeInTheDocument()
    })
  })

  describe('header display', () => {
    it('displays issue ID and title in header', () => {
      const change = createMockChange({
        issueId: 'xyz789',
        title: 'My Test Issue',
      })

      render(<IssueChangeDetailPanel change={change} />)

      expect(screen.getByText('[xyz789]')).toBeInTheDocument()
      expect(screen.getByText('My Test Issue')).toBeInTheDocument()
    })

    it('shows Untitled when title is missing', () => {
      const change = createMockChange({
        title: null,
      })

      render(<IssueChangeDetailPanel change={change} />)

      expect(screen.getByText('Untitled')).toBeInTheDocument()
    })
  })

  describe('value formatting', () => {
    it('shows (empty) for null values', () => {
      const change = createMockChange({
        changeType: ChangeType.UPDATED,
        fieldChanges: [{ fieldName: 'description', oldValue: null, newValue: 'New value' }],
      })

      render(<IssueChangeDetailPanel change={change} />)

      expect(screen.getByText('(empty)')).toBeInTheDocument()
    })

    it('truncates long values', () => {
      const longValue = 'A'.repeat(100)
      const change = createMockChange({
        changeType: ChangeType.UPDATED,
        fieldChanges: [{ fieldName: 'description', oldValue: 'short', newValue: longValue }],
      })

      render(<IssueChangeDetailPanel change={change} />)

      // Should show truncated value with ...
      expect(screen.getByText('A'.repeat(50) + '...')).toBeInTheDocument()
    })
  })
})
