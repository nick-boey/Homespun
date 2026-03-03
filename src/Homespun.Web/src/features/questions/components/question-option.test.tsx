import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QuestionOption } from './question-option'
import type { QuestionOption as QuestionOptionType } from '@/types/signalr'

const mockOption: QuestionOptionType = {
  label: 'Option 1',
  description: 'This is the first option',
}

describe('QuestionOption', () => {
  it('renders option label and description', () => {
    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={false}
        onChange={vi.fn()}
        mode="radio"
      />
    )

    expect(screen.getByText('Option 1')).toBeInTheDocument()
    expect(screen.getByText('This is the first option')).toBeInTheDocument()
  })

  it('renders radio button when mode is radio', () => {
    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={false}
        onChange={vi.fn()}
        mode="radio"
      />
    )

    const input = screen.getByTestId('option-input-Option 1')
    expect(input).toHaveAttribute('type', 'radio')
  })

  it('renders checkbox when mode is checkbox', () => {
    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={false}
        onChange={vi.fn()}
        mode="checkbox"
      />
    )

    const input = screen.getByTestId('option-input-Option 1')
    expect(input).toHaveAttribute('type', 'checkbox')
  })

  it('shows selected state when isSelected is true', () => {
    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={true}
        onChange={vi.fn()}
        mode="radio"
      />
    )

    const input = screen.getByTestId('option-input-Option 1')
    expect(input).toBeChecked()
  })

  it('calls onChange when clicked', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()

    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={false}
        onChange={onChange}
        mode="radio"
      />
    )

    const optionElement = screen.getByTestId('question-option-Option 1')
    await user.click(optionElement)

    expect(onChange).toHaveBeenCalledWith(true)
  })

  it('calls onChange with false when deselecting', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()

    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={true}
        onChange={onChange}
        mode="checkbox"
      />
    )

    const optionElement = screen.getByTestId('question-option-Option 1')
    await user.click(optionElement)

    expect(onChange).toHaveBeenCalledWith(false)
  })

  it('is disabled when disabled prop is true', () => {
    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={false}
        onChange={vi.fn()}
        mode="radio"
        disabled={true}
      />
    )

    const input = screen.getByTestId('option-input-Option 1')
    expect(input).toBeDisabled()
  })

  it('does not call onChange when disabled', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()

    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={false}
        onChange={onChange}
        mode="radio"
        disabled={true}
      />
    )

    const optionElement = screen.getByTestId('question-option-Option 1')
    await user.click(optionElement)

    expect(onChange).not.toHaveBeenCalled()
  })

  it('applies selected styling when selected', () => {
    render(
      <QuestionOption
        option={mockOption}
        questionId="test-question"
        isSelected={true}
        onChange={vi.fn()}
        mode="radio"
      />
    )

    const optionElement = screen.getByTestId('question-option-Option 1')
    expect(optionElement).toHaveClass('border-primary')
  })
})
