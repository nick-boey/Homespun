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

    expect(onDelete).toHaveBeenCalledWith('Test Prompt')
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

  it('displays (project) suffix for override prompts', () => {
    const overridePrompt = { ...mockPrompt, isOverride: true }
    render(<PromptCard prompt={overridePrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    expect(screen.getByText('Test Prompt')).toBeInTheDocument()
    expect(screen.getByText('(project)')).toBeInTheDocument()
  })

  it('does not display (project) suffix when isOverride is false', () => {
    const normalPrompt = { ...mockPrompt, isOverride: false }
    render(<PromptCard prompt={normalPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    expect(screen.getByText('Test Prompt')).toBeInTheDocument()
    expect(screen.queryByText('(project)')).not.toBeInTheDocument()
  })

  it('does not display (project) suffix when isOverride is undefined', () => {
    render(<PromptCard prompt={mockPrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    expect(screen.getByText('Test Prompt')).toBeInTheDocument()
    expect(screen.queryByText('(project)')).not.toBeInTheDocument()
  })

  it('styles (project) suffix with muted color', () => {
    const overridePrompt = { ...mockPrompt, isOverride: true }
    render(<PromptCard prompt={overridePrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    const suffix = screen.getByText('(project)')
    expect(suffix).toHaveClass('text-muted-foreground')
  })

  it('shows Remove override menu item when isOverride is true', async () => {
    const user = userEvent.setup()
    const overridePrompt = { ...mockPrompt, isOverride: true }
    render(
      <PromptCard
        prompt={overridePrompt}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onRemoveOverride={vi.fn()}
      />
    )

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))

    // Should show Remove override option
    expect(screen.getByText('Remove override')).toBeInTheDocument()
  })

  it('does not show Remove override menu item when isOverride is false', async () => {
    const user = userEvent.setup()
    render(
      <PromptCard
        prompt={mockPrompt}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onRemoveOverride={vi.fn()}
      />
    )

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))

    // Should not show Remove override option
    expect(screen.queryByText('Remove override')).not.toBeInTheDocument()
  })

  it('does not show Remove override menu item when onRemoveOverride is not provided', async () => {
    const user = userEvent.setup()
    const overridePrompt = { ...mockPrompt, isOverride: true }
    render(<PromptCard prompt={overridePrompt} onEdit={vi.fn()} onDelete={vi.fn()} />)

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))

    // Should not show Remove override option
    expect(screen.queryByText('Remove override')).not.toBeInTheDocument()
  })

  it('shows Remove override confirmation dialog', async () => {
    const user = userEvent.setup()
    const overridePrompt = { ...mockPrompt, isOverride: true }
    render(
      <PromptCard
        prompt={overridePrompt}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onRemoveOverride={vi.fn()}
      />
    )

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))
    // Click Remove override
    await user.click(screen.getByText('Remove override'))

    // Should show confirmation dialog
    expect(screen.getByText('Remove Override')).toBeInTheDocument()
    expect(
      screen.getByText(
        /This will remove the project-specific prompt and revert to the global prompt/
      )
    ).toBeInTheDocument()
  })

  it('calls onRemoveOverride when Remove override is confirmed', async () => {
    const user = userEvent.setup()
    const overridePrompt = { ...mockPrompt, isOverride: true }
    const onRemoveOverride = vi.fn()
    render(
      <PromptCard
        prompt={overridePrompt}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onRemoveOverride={onRemoveOverride}
      />
    )

    // Open dropdown and click Remove override
    await user.click(screen.getByRole('button', { name: /actions/i }))
    await user.click(screen.getByText('Remove override'))

    // Confirm removal
    await user.click(screen.getByRole('button', { name: /^Remove$/i }))

    expect(onRemoveOverride).toHaveBeenCalledWith('Test Prompt')
  })

  it('closes Remove override dialog when cancelled', async () => {
    const user = userEvent.setup()
    const overridePrompt = { ...mockPrompt, isOverride: true }
    const onRemoveOverride = vi.fn()
    render(
      <PromptCard
        prompt={overridePrompt}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onRemoveOverride={onRemoveOverride}
      />
    )

    // Open dropdown and click Remove override
    await user.click(screen.getByRole('button', { name: /actions/i }))
    await user.click(screen.getByText('Remove override'))

    // Cancel
    await user.click(screen.getByRole('button', { name: /Cancel/i }))

    // Dialog should be closed and onRemoveOverride should not be called
    expect(screen.queryByText('Remove Override')).not.toBeInTheDocument()
    expect(onRemoveOverride).not.toHaveBeenCalled()
  })

  it('shows removing state when isRemovingOverride is true', async () => {
    const user = userEvent.setup()
    const overridePrompt = { ...mockPrompt, isOverride: true }
    render(
      <PromptCard
        prompt={overridePrompt}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onRemoveOverride={vi.fn()}
        isRemovingOverride={true}
      />
    )

    // Open dropdown and click Remove override
    await user.click(screen.getByRole('button', { name: /actions/i }))
    await user.click(screen.getByText('Remove override'))

    // Should show "Removing..." button text
    expect(screen.getByRole('button', { name: /Removing/i })).toBeInTheDocument()
  })
})
