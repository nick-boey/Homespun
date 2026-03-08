import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QuestionPanel } from './question-panel'
import type { PendingQuestion } from '@/types/signalr'

const createPendingQuestion = (overrides?: Partial<PendingQuestion>): PendingQuestion => ({
  id: 'question-123',
  toolUseId: 'tool-456',
  questions: [
    {
      question: 'Which framework do you prefer?',
      header: 'Framework',
      options: [
        { label: 'React', description: 'A JavaScript library for building UIs' },
        { label: 'Vue', description: 'The progressive JavaScript framework' },
        { label: 'Angular', description: 'A platform for building web apps' },
      ],
      multiSelect: false,
    },
  ],
  createdAt: '2024-01-01T12:00:00Z',
  ...overrides,
})

describe('QuestionPanel', () => {
  it('renders question panel with title', () => {
    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    expect(screen.getByText(/Claude has a question for you/i)).toBeInTheDocument()
    expect(screen.getByText(/Please answer the following/i)).toBeInTheDocument()
  })

  it('renders question text and header', () => {
    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    expect(screen.getByText('Framework')).toBeInTheDocument()
    expect(screen.getByText('Which framework do you prefer?')).toBeInTheDocument()
    // Verify header is rendered as a heading, not a badge
    const header = screen.getByText('Framework')
    expect(header.tagName).toBe('H4')
  })

  it('renders all options', () => {
    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    expect(screen.getByText('React')).toBeInTheDocument()
    expect(screen.getByText('Vue')).toBeInTheDocument()
    expect(screen.getByText('Angular')).toBeInTheDocument()
    expect(screen.getByText('A JavaScript library for building UIs')).toBeInTheDocument()
  })

  it('renders "Other" option', () => {
    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    expect(screen.getByText('Other...')).toBeInTheDocument()
  })

  it('submit button is disabled when no option is selected', () => {
    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    const submitButton = screen.getByTestId('submit-answers-button')
    expect(submitButton).toBeDisabled()
  })

  it('submit button is enabled when an option is selected', async () => {
    const user = userEvent.setup()

    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    // Click on the option container by testid
    await user.click(screen.getByTestId('question-option-React'))

    const submitButton = screen.getByTestId('submit-answers-button')
    expect(submitButton).not.toBeDisabled()
  })

  it('calls onSubmit with selected answer', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockResolvedValue(undefined)

    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={onSubmit} />)

    await user.click(screen.getByTestId('question-option-React'))
    await user.click(screen.getByTestId('submit-answers-button'))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        'Which framework do you prefer?': 'React',
      })
    })
  })

  it('shows text input when "Other" is selected', async () => {
    const user = userEvent.setup()

    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    // Click on "Other" option via testid
    await user.click(screen.getByTestId('other-option'))

    expect(screen.getByTestId('other-text-input')).toBeInTheDocument()
  })

  it('submit button is disabled when "Other" is selected but text is empty', async () => {
    const user = userEvent.setup()

    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    await user.click(screen.getByTestId('other-option'))

    const submitButton = screen.getByTestId('submit-answers-button')
    expect(submitButton).toBeDisabled()
  })

  it('submit button is enabled when "Other" has text', async () => {
    const user = userEvent.setup()

    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={vi.fn()} />)

    await user.click(screen.getByTestId('other-option'))
    await user.type(screen.getByTestId('other-text-input'), 'Svelte')

    const submitButton = screen.getByTestId('submit-answers-button')
    expect(submitButton).not.toBeDisabled()
  })

  it('submits "Other" text as answer', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockResolvedValue(undefined)

    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={onSubmit} />)

    await user.click(screen.getByTestId('other-option'))
    await user.type(screen.getByTestId('other-text-input'), 'Svelte')
    await user.click(screen.getByTestId('submit-answers-button'))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        'Which framework do you prefer?': 'Svelte',
      })
    })
  })

  it('handles multi-select questions with checkboxes', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockResolvedValue(undefined)

    const multiSelectQuestion = createPendingQuestion({
      questions: [
        {
          question: 'Which features do you want?',
          header: 'Features',
          options: [
            { label: 'Auth', description: 'User authentication' },
            { label: 'Database', description: 'Database integration' },
            { label: 'API', description: 'REST API support' },
          ],
          multiSelect: true,
        },
      ],
    })

    render(<QuestionPanel pendingQuestion={multiSelectQuestion} onSubmit={onSubmit} />)

    // Select multiple options using testid
    await user.click(screen.getByTestId('question-option-Auth'))
    await user.click(screen.getByTestId('question-option-Database'))
    await user.click(screen.getByTestId('submit-answers-button'))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        'Which features do you want?': 'Auth, Database',
      })
    })
  })

  it('shows "You can select multiple options" hint for multi-select', () => {
    const multiSelectQuestion = createPendingQuestion({
      questions: [
        {
          question: 'Which features do you want?',
          header: 'Features',
          options: [{ label: 'Auth', description: 'User authentication' }],
          multiSelect: true,
        },
      ],
    })

    render(<QuestionPanel pendingQuestion={multiSelectQuestion} onSubmit={vi.fn()} />)

    expect(screen.getByText(/You can select multiple options/i)).toBeInTheDocument()
  })

  it('shows loading state when isSubmitting is true', () => {
    render(
      <QuestionPanel
        pendingQuestion={createPendingQuestion()}
        onSubmit={vi.fn()}
        isSubmitting={true}
      />
    )

    expect(screen.getByText(/Submitting/i)).toBeInTheDocument()
    const submitButton = screen.getByTestId('submit-answers-button')
    expect(submitButton).toBeDisabled()
  })

  it('handles multiple questions', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockResolvedValue(undefined)

    const multipleQuestions = createPendingQuestion({
      questions: [
        {
          question: 'Question 1',
          header: 'Q1',
          options: [
            { label: 'A1', description: 'Answer 1' },
            { label: 'B1', description: 'Answer 2' },
          ],
          multiSelect: false,
        },
        {
          question: 'Question 2',
          header: 'Q2',
          options: [
            { label: 'A2', description: 'Answer 1' },
            { label: 'B2', description: 'Answer 2' },
          ],
          multiSelect: false,
        },
      ],
    })

    render(<QuestionPanel pendingQuestion={multipleQuestions} onSubmit={onSubmit} />)

    // Submit should be disabled until both questions are answered
    expect(screen.getByTestId('submit-answers-button')).toBeDisabled()

    // Answer first question only
    await user.click(screen.getByTestId('question-option-A1'))
    expect(screen.getByTestId('submit-answers-button')).toBeDisabled()

    // Answer second question
    await user.click(screen.getByTestId('question-option-A2'))
    expect(screen.getByTestId('submit-answers-button')).not.toBeDisabled()

    await user.click(screen.getByTestId('submit-answers-button'))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        'Question 1': 'A1',
        'Question 2': 'A2',
      })
    })
  })

  it('single-select clears previous selection when new option is selected', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockResolvedValue(undefined)

    render(<QuestionPanel pendingQuestion={createPendingQuestion()} onSubmit={onSubmit} />)

    // Select React first
    await user.click(screen.getByTestId('question-option-React'))
    // Then select Vue
    await user.click(screen.getByTestId('question-option-Vue'))

    await user.click(screen.getByTestId('submit-answers-button'))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        'Which framework do you prefer?': 'Vue',
      })
    })
  })
})
