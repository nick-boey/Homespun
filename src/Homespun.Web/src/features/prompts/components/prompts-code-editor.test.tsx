import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PromptsCodeEditor } from './prompts-code-editor'
import { SessionMode } from '@/api/generated/types.gen'
import type { AgentPrompt } from '@/api/generated/types.gen'

const mockPrompts: AgentPrompt[] = [
  {
    id: 'prompt-1',
    name: 'Test Prompt',
    initialMessage: 'Hello world',
    mode: SessionMode.BUILD,
  },
  {
    id: 'prompt-2',
    name: 'Another Prompt',
    initialMessage: 'Another message',
    mode: SessionMode.PLAN,
  },
]

// Helper to set textarea value directly (avoids userEvent parsing issues with special characters)
function setTextareaValue(textarea: HTMLTextAreaElement, value: string) {
  fireEvent.change(textarea, { target: { value } })
}

describe('PromptsCodeEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('displays formatted JSON in textarea', () => {
    render(<PromptsCodeEditor prompts={mockPrompts} onApply={vi.fn()} isApplying={false} />)

    const textarea = screen.getByRole('textbox')
    const value = textarea.getAttribute('value') ?? (textarea as HTMLTextAreaElement).value

    // Should contain the prompt names
    expect(value).toContain('Test Prompt')
    expect(value).toContain('Another Prompt')

    // Should be formatted JSON with indentation
    expect(value).toContain('\n')
  })

  it('renders Apply and Revert buttons', () => {
    render(<PromptsCodeEditor prompts={mockPrompts} onApply={vi.fn()} isApplying={false} />)

    expect(screen.getByRole('button', { name: /apply/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /revert/i })).toBeInTheDocument()
  })

  it('shows inline error for invalid JSON', async () => {
    const user = userEvent.setup()
    render(<PromptsCodeEditor prompts={mockPrompts} onApply={vi.fn()} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    setTextareaValue(textarea, 'not valid json')

    await user.click(screen.getByRole('button', { name: /apply/i }))

    await waitFor(() => {
      expect(screen.getByText(/invalid json/i)).toBeInTheDocument()
    })
  })

  it('shows error for missing required field', async () => {
    const user = userEvent.setup()
    render(<PromptsCodeEditor prompts={mockPrompts} onApply={vi.fn()} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    // JSON missing required 'mode' field
    setTextareaValue(textarea, '[{"id": "1", "name": "Test"}]')

    await user.click(screen.getByRole('button', { name: /apply/i }))

    await waitFor(() => {
      expect(screen.getByText(/mode/i)).toBeInTheDocument()
    })
  })

  it('calls onApply with changes when Apply is clicked and JSON is valid', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn().mockResolvedValue(undefined)
    render(<PromptsCodeEditor prompts={[]} onApply={onApply} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    setTextareaValue(textarea, '[{"name": "New Prompt", "mode": "build"}]')

    await user.click(screen.getByRole('button', { name: /apply/i }))

    await waitFor(() => {
      expect(onApply).toHaveBeenCalledWith(
        expect.objectContaining({
          creates: expect.arrayContaining([
            expect.objectContaining({ name: 'New Prompt', mode: 'build' }),
          ]),
        })
      )
    })
  })

  it('revert resets textarea to original JSON', async () => {
    const user = userEvent.setup()
    render(<PromptsCodeEditor prompts={mockPrompts} onApply={vi.fn()} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    const originalValue = textarea.value

    // Modify the textarea using fireEvent
    setTextareaValue(textarea, 'modified content')

    expect(textarea.value).toBe('modified content')

    // Click Revert
    await user.click(screen.getByRole('button', { name: /revert/i }))

    // Should be back to original
    expect(textarea.value).toBe(originalValue)
  })

  it('disables Apply button when isApplying is true', () => {
    render(<PromptsCodeEditor prompts={mockPrompts} onApply={vi.fn()} isApplying={true} />)

    expect(screen.getByRole('button', { name: /apply/i })).toBeDisabled()
  })

  it('shows loading state on Apply button when isApplying is true', () => {
    render(<PromptsCodeEditor prompts={mockPrompts} onApply={vi.fn()} isApplying={true} />)

    // Should show "Applying..." or similar
    expect(screen.getByText(/applying/i)).toBeInTheDocument()
  })

  it('clears error message when user starts editing after error', async () => {
    const user = userEvent.setup()
    render(<PromptsCodeEditor prompts={mockPrompts} onApply={vi.fn()} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement

    // Trigger an error
    setTextareaValue(textarea, 'invalid')
    await user.click(screen.getByRole('button', { name: /apply/i }))

    await waitFor(() => {
      expect(screen.getByText(/invalid json/i)).toBeInTheDocument()
    })

    // Start typing again - modify the textarea
    setTextareaValue(textarea, '[]')

    // Error should be cleared
    await waitFor(() => {
      expect(screen.queryByText(/invalid json/i)).not.toBeInTheDocument()
    })
  })

  it('shows delete confirmation when changes include deletes', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn().mockResolvedValue(undefined)

    render(<PromptsCodeEditor prompts={mockPrompts} onApply={onApply} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    // Clear to remove all prompts (will create deletes)
    setTextareaValue(textarea, '[]')

    await user.click(screen.getByRole('button', { name: /apply/i }))

    // Should show confirmation dialog
    await waitFor(() => {
      expect(screen.getByText(/confirm deletion/i)).toBeInTheDocument()
    })

    // onApply should NOT have been called yet
    expect(onApply).not.toHaveBeenCalled()
  })

  it('calls onApply after confirming deletion', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn().mockResolvedValue(undefined)

    render(<PromptsCodeEditor prompts={mockPrompts} onApply={onApply} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    setTextareaValue(textarea, '[]')

    await user.click(screen.getByRole('button', { name: /apply/i }))

    // Wait for confirmation dialog
    await waitFor(() => {
      expect(screen.getByText(/confirm deletion/i)).toBeInTheDocument()
    })

    // Confirm the deletion
    await user.click(screen.getByRole('button', { name: /^confirm$/i }))

    // Now onApply should be called
    await waitFor(() => {
      expect(onApply).toHaveBeenCalled()
    })
  })

  it('does not call onApply when deletion is cancelled', async () => {
    const user = userEvent.setup()
    const onApply = vi.fn().mockResolvedValue(undefined)

    render(<PromptsCodeEditor prompts={mockPrompts} onApply={onApply} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    setTextareaValue(textarea, '[]')

    await user.click(screen.getByRole('button', { name: /apply/i }))

    // Wait for confirmation dialog
    await waitFor(() => {
      expect(screen.getByText(/confirm deletion/i)).toBeInTheDocument()
    })

    // Cancel the deletion
    await user.click(screen.getByRole('button', { name: /cancel/i }))

    // onApply should NOT be called
    expect(onApply).not.toHaveBeenCalled()

    // Dialog should be closed
    await waitFor(() => {
      expect(screen.queryByText(/confirm deletion/i)).not.toBeInTheDocument()
    })
  })

  it('handles empty prompts array', () => {
    render(<PromptsCodeEditor prompts={[]} onApply={vi.fn()} isApplying={false} />)

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    expect(textarea.value).toBe('[]')
  })
})
