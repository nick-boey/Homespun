import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SecretRow } from './secret-row'
import type { SecretInfo } from '@/api/generated/types.gen'

const mockSecret: SecretInfo = {
  name: 'API_KEY',
  lastModified: '2024-01-15T10:30:00Z',
}

describe('SecretRow', () => {
  const defaultProps = {
    secret: mockSecret,
    onEdit: vi.fn(),
    onDelete: vi.fn(),
    isDeleting: false,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders secret name', () => {
    render(<SecretRow {...defaultProps} />)

    expect(screen.getByText('API_KEY')).toBeInTheDocument()
  })

  it('renders masked value indicator', () => {
    render(<SecretRow {...defaultProps} />)

    expect(screen.getByText('••••••••')).toBeInTheDocument()
  })

  it('renders last modified time', () => {
    render(<SecretRow {...defaultProps} />)

    // Check for some time-related content (the exact format may vary)
    expect(screen.getByText(/Jan 15, 2024/i)).toBeInTheDocument()
  })

  it('handles missing lastModified gracefully', () => {
    const secretWithoutDate: SecretInfo = { name: 'SECRET_KEY' }
    render(<SecretRow {...defaultProps} secret={secretWithoutDate} />)

    expect(screen.getByText('SECRET_KEY')).toBeInTheDocument()
  })

  it('shows delete confirmation dialog when delete button is clicked', async () => {
    const user = userEvent.setup()
    render(<SecretRow {...defaultProps} />)

    const deleteButton = screen.getByRole('button', { name: /delete/i })
    await user.click(deleteButton)

    expect(screen.getByText('Delete Secret')).toBeInTheDocument()
    expect(screen.getByText(/Are you sure you want to delete/)).toBeInTheDocument()
  })

  it('calls onDelete when deletion is confirmed', async () => {
    const user = userEvent.setup()
    render(<SecretRow {...defaultProps} />)

    const deleteButton = screen.getByRole('button', { name: /delete/i })
    await user.click(deleteButton)

    const confirmButton = screen.getByRole('button', { name: 'Delete' })
    await user.click(confirmButton)

    expect(defaultProps.onDelete).toHaveBeenCalledTimes(1)
  })

  it('does not call onDelete when deletion is cancelled', async () => {
    const user = userEvent.setup()
    render(<SecretRow {...defaultProps} />)

    const deleteButton = screen.getByRole('button', { name: /delete/i })
    await user.click(deleteButton)

    const cancelButton = screen.getByRole('button', { name: 'Cancel' })
    await user.click(cancelButton)

    expect(defaultProps.onDelete).not.toHaveBeenCalled()
  })

  it('calls onEdit when edit button is clicked', async () => {
    const user = userEvent.setup()
    render(<SecretRow {...defaultProps} />)

    const editButton = screen.getByRole('button', { name: /edit/i })
    await user.click(editButton)

    expect(defaultProps.onEdit).toHaveBeenCalledTimes(1)
  })

  it('disables delete button when isDeleting is true', () => {
    render(<SecretRow {...defaultProps} isDeleting={true} />)

    const deleteButton = screen.getByRole('button', { name: /delete/i })
    expect(deleteButton).toBeDisabled()
  })

  it('displays secret name in delete confirmation dialog', async () => {
    const user = userEvent.setup()
    render(<SecretRow {...defaultProps} />)

    const deleteButton = screen.getByRole('button', { name: /delete/i })
    await user.click(deleteButton)

    // The dialog description should contain the secret name
    expect(
      screen.getByText(/Are you sure you want to delete the secret "API_KEY"\?/)
    ).toBeInTheDocument()
  })
})
