import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MentionSearchPopup, type MentionSearchPopupProps } from './mention-search-popup'

describe('MentionSearchPopup', () => {
  const defaultProps: MentionSearchPopupProps = {
    open: true,
    triggerType: '@',
    query: '',
    files: ['src/index.ts', 'src/utils.ts', 'package.json'],
    prs: [
      { number: 123, title: 'Add feature X', branchName: 'feature/x' },
      { number: 456, title: 'Fix bug Y', branchName: 'fix/y' },
    ],
    onSelect: vi.fn(),
    onClose: vi.fn(),
    isLoadingFiles: false,
    isLoadingPrs: false,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('rendering', () => {
    it('renders nothing when closed', () => {
      render(<MentionSearchPopup {...defaultProps} open={false} />)
      expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    })

    it('renders file list for @ trigger', () => {
      render(<MentionSearchPopup {...defaultProps} />)
      expect(screen.getByText('src/index.ts')).toBeInTheDocument()
      expect(screen.getByText('src/utils.ts')).toBeInTheDocument()
      expect(screen.getByText('package.json')).toBeInTheDocument()
    })

    it('renders PR list for # trigger', () => {
      render(<MentionSearchPopup {...defaultProps} triggerType="#" />)
      expect(screen.getByText('#123')).toBeInTheDocument()
      expect(screen.getByText('Add feature X')).toBeInTheDocument()
      expect(screen.getByText('#456')).toBeInTheDocument()
    })

    it('shows loading state for files', () => {
      render(<MentionSearchPopup {...defaultProps} isLoadingFiles files={[]} />)
      expect(screen.getByTestId('search-loading')).toBeInTheDocument()
    })

    it('shows loading state for PRs', () => {
      render(<MentionSearchPopup {...defaultProps} triggerType="#" isLoadingPrs prs={[]} />)
      expect(screen.getByTestId('search-loading')).toBeInTheDocument()
    })

    it('shows empty state when no files match', () => {
      render(<MentionSearchPopup {...defaultProps} files={[]} query="nonexistent" />)
      expect(screen.getByText(/no files found/i)).toBeInTheDocument()
    })

    it('shows empty state when no PRs match', () => {
      render(<MentionSearchPopup {...defaultProps} triggerType="#" prs={[]} query="nonexistent" />)
      expect(screen.getByText(/no pull requests found/i)).toBeInTheDocument()
    })
  })

  describe('selection', () => {
    it('calls onSelect with file path when file clicked', async () => {
      const user = userEvent.setup()
      const onSelect = vi.fn()
      render(<MentionSearchPopup {...defaultProps} onSelect={onSelect} />)

      await user.click(screen.getByText('src/index.ts'))
      expect(onSelect).toHaveBeenCalledWith({ type: '@', value: 'src/index.ts' })
    })

    it('calls onSelect with PR number when PR clicked', async () => {
      const user = userEvent.setup()
      const onSelect = vi.fn()
      render(<MentionSearchPopup {...defaultProps} triggerType="#" onSelect={onSelect} />)

      await user.click(screen.getByText('#123'))
      expect(onSelect).toHaveBeenCalledWith({ type: '#', value: '123' })
    })
  })

  describe('keyboard navigation', () => {
    it('calls onClose when Escape pressed', () => {
      const onClose = vi.fn()
      render(<MentionSearchPopup {...defaultProps} onClose={onClose} />)

      fireEvent.keyDown(document.body, { key: 'Escape' })
      expect(onClose).toHaveBeenCalled()
    })
  })

  describe('limits', () => {
    it('limits displayed files to 20', () => {
      const manyFiles = Array.from({ length: 30 }, (_, i) => `file${i}.ts`)
      render(<MentionSearchPopup {...defaultProps} files={manyFiles} />)

      // Should only show 20 items
      const items = screen.getAllByRole('option')
      expect(items).toHaveLength(20)
    })

    it('limits displayed PRs to 20', () => {
      const manyPrs = Array.from({ length: 30 }, (_, i) => ({
        number: i + 1,
        title: `PR ${i + 1}`,
        branchName: `branch-${i + 1}`,
      }))
      render(<MentionSearchPopup {...defaultProps} triggerType="#" prs={manyPrs} />)

      const items = screen.getAllByRole('option')
      expect(items).toHaveLength(20)
    })
  })
})
