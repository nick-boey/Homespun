import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PromptForm } from './prompt-form'
import type { AgentPrompt } from '@/api/generated/types.gen'

describe('PromptForm', () => {
  it('renders empty form for creating new prompt', () => {
    render(<PromptForm onSubmit={vi.fn()} onCancel={vi.fn()} />)

    expect(screen.getByLabelText('Name')).toHaveValue('')
    expect(screen.getByLabelText('System Prompt')).toHaveValue('')
    expect(screen.getByText('Create Prompt')).toBeInTheDocument()
  })

  it('populates form with existing prompt data', () => {
    const existingPrompt: AgentPrompt = {
      id: 'prompt-1',
      name: 'Existing Prompt',
      initialMessage: 'Existing content',
      mode: 0,
    }
    render(<PromptForm prompt={existingPrompt} onSubmit={vi.fn()} onCancel={vi.fn()} />)

    expect(screen.getByLabelText('Name')).toHaveValue('Existing Prompt')
    expect(screen.getByLabelText('System Prompt')).toHaveValue('Existing content')
    expect(screen.getByText('Update Prompt')).toBeInTheDocument()
  })

  it('validates required name field', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<PromptForm onSubmit={onSubmit} onCancel={vi.fn()} />)

    // Try to submit without name
    await user.click(screen.getByText('Create Prompt'))

    await waitFor(() => {
      expect(screen.getByText('Prompt name is required')).toBeInTheDocument()
    })
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('calls onSubmit with form data', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    render(<PromptForm onSubmit={onSubmit} onCancel={vi.fn()} />)

    await user.type(screen.getByLabelText('Name'), 'My New Prompt')
    await user.type(screen.getByLabelText('System Prompt'), 'System prompt content')
    await user.click(screen.getByText('Create Prompt'))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        name: 'My New Prompt',
        initialMessage: 'System prompt content',
        mode: 1,
      })
    })
  })

  it('calls onCancel when cancel button is clicked', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    render(<PromptForm onSubmit={vi.fn()} onCancel={onCancel} />)

    await user.click(screen.getByText('Cancel'))

    expect(onCancel).toHaveBeenCalled()
  })

  it('toggles between edit and preview mode', async () => {
    const user = userEvent.setup()
    render(<PromptForm onSubmit={vi.fn()} onCancel={vi.fn()} />)

    // Initially in edit mode
    expect(screen.getByLabelText('System Prompt')).toBeInTheDocument()
    expect(screen.getByText('Preview')).toBeInTheDocument()

    // Switch to preview mode
    await user.click(screen.getByText('Preview'))

    // Should show preview text and Edit button
    expect(screen.getByText('No content to preview')).toBeInTheDocument()
    expect(screen.getByText('Edit')).toBeInTheDocument()
  })

  it('shows saving state when isSubmitting is true', () => {
    render(<PromptForm onSubmit={vi.fn()} onCancel={vi.fn()} isSubmitting={true} />)

    expect(screen.getByText('Saving...')).toBeInTheDocument()
    expect(screen.getByText('Saving...')).toBeDisabled()
    expect(screen.getByText('Cancel')).toBeDisabled()
  })
})
