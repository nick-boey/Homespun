import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SecretFormDialog } from './secret-form-dialog'

describe('SecretFormDialog', () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    onSubmit: vi.fn(),
    isSubmitting: false,
    mode: 'create' as const,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('create mode', () => {
    it('renders create dialog title', () => {
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      expect(screen.getByText('Add Secret')).toBeInTheDocument()
    })

    it('renders name input field', () => {
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      expect(screen.getByLabelText(/name/i)).toBeInTheDocument()
    })

    it('renders value input field as password type', () => {
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      const valueInput = screen.getByLabelText(/value/i)
      expect(valueInput).toHaveAttribute('type', 'password')
    })

    it('submits form with name and value', async () => {
      const user = userEvent.setup()
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      const nameInput = screen.getByLabelText(/name/i)
      const valueInput = screen.getByLabelText(/value/i)

      await user.type(nameInput, 'API_KEY')
      await user.type(valueInput, 'my-secret-value')

      const submitButton = screen.getByRole('button', { name: /add/i })
      await user.click(submitButton)

      expect(defaultProps.onSubmit).toHaveBeenCalledWith({
        name: 'API_KEY',
        value: 'my-secret-value',
      })
    })

    it('validates name format for environment variable', async () => {
      const user = userEvent.setup()
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      const nameInput = screen.getByLabelText(/name/i)
      const valueInput = screen.getByLabelText(/value/i)

      await user.type(nameInput, '123-invalid-name')
      await user.type(valueInput, 'value')

      const submitButton = screen.getByRole('button', { name: /add/i })
      await user.click(submitButton)

      expect(defaultProps.onSubmit).not.toHaveBeenCalled()
      expect(screen.getByText(/must start with a letter or underscore/i)).toBeInTheDocument()
    })

    it('requires name field', async () => {
      const user = userEvent.setup()
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      const valueInput = screen.getByLabelText(/value/i)
      await user.type(valueInput, 'value')

      const submitButton = screen.getByRole('button', { name: /add/i })
      await user.click(submitButton)

      expect(defaultProps.onSubmit).not.toHaveBeenCalled()
    })

    it('requires value field', async () => {
      const user = userEvent.setup()
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      const nameInput = screen.getByLabelText(/name/i)
      await user.type(nameInput, 'API_KEY')

      const submitButton = screen.getByRole('button', { name: /add/i })
      await user.click(submitButton)

      expect(defaultProps.onSubmit).not.toHaveBeenCalled()
    })

    it('closes dialog when cancel is clicked', async () => {
      const user = userEvent.setup()
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      const cancelButton = screen.getByRole('button', { name: /cancel/i })
      await user.click(cancelButton)

      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false)
    })

    it('disables submit button when isSubmitting is true', () => {
      render(<SecretFormDialog {...defaultProps} mode="create" isSubmitting={true} />)

      const submitButton = screen.getByRole('button', { name: /add/i })
      expect(submitButton).toBeDisabled()
    })

    it('shows security warning about values not being retrievable', () => {
      render(<SecretFormDialog {...defaultProps} mode="create" />)

      expect(screen.getByText(/secret values cannot be viewed after saving/i)).toBeInTheDocument()
    })
  })

  describe('edit mode', () => {
    const editProps = {
      ...defaultProps,
      mode: 'edit' as const,
      secretName: 'API_KEY',
    }

    it('renders edit dialog title', () => {
      render(<SecretFormDialog {...editProps} />)

      expect(screen.getByText('Update Secret')).toBeInTheDocument()
    })

    it('displays secret name but does not allow editing it', () => {
      render(<SecretFormDialog {...editProps} />)

      expect(screen.getByText('API_KEY')).toBeInTheDocument()
      // Name input should not be present in edit mode
      expect(screen.queryByLabelText(/^name$/i)).not.toBeInTheDocument()
    })

    it('renders only value input field', () => {
      render(<SecretFormDialog {...editProps} />)

      expect(screen.getByLabelText(/new value/i)).toBeInTheDocument()
    })

    it('submits form with new value only', async () => {
      const user = userEvent.setup()
      render(<SecretFormDialog {...editProps} />)

      const valueInput = screen.getByLabelText(/new value/i)
      await user.type(valueInput, 'updated-secret-value')

      const submitButton = screen.getByRole('button', { name: /update/i })
      await user.click(submitButton)

      expect(editProps.onSubmit).toHaveBeenCalledWith({
        value: 'updated-secret-value',
      })
    })

    it('requires value field in edit mode', async () => {
      const user = userEvent.setup()
      render(<SecretFormDialog {...editProps} />)

      const submitButton = screen.getByRole('button', { name: /update/i })
      await user.click(submitButton)

      expect(editProps.onSubmit).not.toHaveBeenCalled()
    })
  })

  it('does not render when open is false', () => {
    render(<SecretFormDialog {...defaultProps} open={false} />)

    expect(screen.queryByText('Add Secret')).not.toBeInTheDocument()
  })

  it('clears form when dialog reopens', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<SecretFormDialog {...defaultProps} mode="create" />)

    const nameInput = screen.getByLabelText(/name/i)
    const valueInput = screen.getByLabelText(/value/i)

    await user.type(nameInput, 'API_KEY')
    await user.type(valueInput, 'secret')

    // Close and reopen
    rerender(<SecretFormDialog {...defaultProps} mode="create" open={false} />)
    rerender(<SecretFormDialog {...defaultProps} mode="create" open={true} />)

    await waitFor(() => {
      const newNameInput = screen.getByLabelText(/name/i) as HTMLInputElement
      expect(newNameInput.value).toBe('')
    })
  })
})
