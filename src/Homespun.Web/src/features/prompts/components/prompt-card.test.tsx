import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PromptCard } from './prompt-card'
import type { AgentPrompt } from '@/api/generated/types.gen'
import { SessionMode } from '@/api/generated/types.gen'

const mockPrompt: AgentPrompt = {
  id: 'prompt-1',
  name: 'Test Prompt',
  initialMessage: 'This is a test system prompt with some content.',
  mode: SessionMode.BUILD,
  projectId: 'proj-1',
  updatedAt: '2024-01-15T10:00:00Z',
}

describe('PromptCard', () => {
  it('renders prompt name and content preview', () => {
    render(<PromptCard prompt={mockPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    expect(screen.getByText('Test Prompt')).toBeInTheDocument()
    expect(screen.getByText('This is a test system prompt with some content.')).toBeInTheDocument()
  })

  it('displays Build mode badge for mode 1', () => {
    render(<PromptCard prompt={mockPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    expect(screen.getByText('Build')).toBeInTheDocument()
  })

  it('displays Plan mode badge for mode Plan', () => {
    const planPrompt = { ...mockPrompt, mode: SessionMode.PLAN }
    render(<PromptCard prompt={planPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    expect(screen.getByText('Plan')).toBeInTheDocument()
  })

  it('calls onEdit when edit action is clicked', async () => {
    const user = userEvent.setup()
    const onEdit = vi.fn()
    render(<PromptCard prompt={mockPrompt} onEdit={onEdit} onDelete={vi.fn()} />)

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))
    // Click edit
    await user.click(screen.getByText('Edit'))

    expect(onEdit).toHaveBeenCalledWith(mockPrompt)
  })

  it('shows delete confirmation dialog', async () => {
    const user = userEvent.setup()
    render(<PromptCard prompt={mockPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))
    // Click delete
    await user.click(screen.getByText('Delete'))

    // Should show confirmation dialog
    expect(screen.getByText('Delete Prompt')).toBeInTheDocument()
    expect(screen.getByText(/Are you sure you want to delete "Test Prompt"/)).toBeInTheDocument()
  })

  it('calls onDelete when delete is confirmed', async () => {
    const user = userEvent.setup()
    const onDelete = vi.fn()
    render(<PromptCard prompt={mockPrompt} onEdit={vi.fn()} onDelete={onDelete} />)

    // Open dropdown and click delete
    await user.click(screen.getByRole('button', { name: /actions/i }))
    await user.click(screen.getByText('Delete'))

    // Confirm deletion
    await user.click(screen.getByRole('button', { name: /^Delete$/i }))

    expect(onDelete).toHaveBeenCalledWith('prompt-1')
  })

  it('truncates long content preview', () => {
    const longPrompt = {
      ...mockPrompt,
      initialMessage: 'A'.repeat(200),
    }
    render(<PromptCard prompt={longPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    // Should show truncated text with ellipsis
    const previewText = screen.getByText(/^A+\.\.\./)
    expect(previewText).toBeInTheDocument()
  })

  it('displays Global badge for global prompts', () => {
    const globalPrompt = { ...mockPrompt, projectId: null }
    render(<PromptCard prompt={globalPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    expect(screen.getByText('Global')).toBeInTheDocument()
  })

  it('does not display badge for project prompts', () => {
    render(<PromptCard prompt={mockPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    // Should not have Global badge
    expect(screen.queryByText('Global')).not.toBeInTheDocument()
  })
})
