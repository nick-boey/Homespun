import { useState, useCallback, useMemo } from 'react'
import {
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardFooter,
} from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { QuestionOption } from './question-option'
import type { PendingQuestion, UserQuestion } from '@/types/signalr'
import { MessageCircleQuestion, Loader2 } from 'lucide-react'

export interface QuestionPanelProps {
  pendingQuestion: PendingQuestion
  onSubmit: (answers: Record<string, string>) => Promise<void>
  isSubmitting?: boolean
}

interface QuestionAnswerState {
  selectedOptions: string[]
  otherText: string
  isOtherSelected: boolean
}

const OTHER_OPTION = '__other__'

export function QuestionPanel({
  pendingQuestion,
  onSubmit,
  isSubmitting = false,
}: QuestionPanelProps) {
  const [answerStates, setAnswerStates] = useState<Record<string, QuestionAnswerState>>(() => {
    const initial: Record<string, QuestionAnswerState> = {}
    for (const question of pendingQuestion.questions) {
      initial[question.question] = {
        selectedOptions: [],
        otherText: '',
        isOtherSelected: false,
      }
    }
    return initial
  })

  const handleOptionChange = useCallback(
    (question: UserQuestion, optionLabel: string, selected: boolean) => {
      setAnswerStates((prev) => {
        const current = prev[question.question]
        let newSelectedOptions: string[]

        if (optionLabel === OTHER_OPTION) {
          // Handle "Other" option
          return {
            ...prev,
            [question.question]: {
              ...current,
              isOtherSelected: selected,
              // Clear regular selection when selecting "Other" in single-select mode
              selectedOptions: question.multiSelect ? current.selectedOptions : [],
            },
          }
        }

        if (question.multiSelect) {
          // Multi-select: toggle the option
          newSelectedOptions = selected
            ? [...current.selectedOptions, optionLabel]
            : current.selectedOptions.filter((o) => o !== optionLabel)
        } else {
          // Single-select: replace the selection
          newSelectedOptions = selected ? [optionLabel] : []
        }

        return {
          ...prev,
          [question.question]: {
            ...current,
            selectedOptions: newSelectedOptions,
            // Clear "Other" when selecting a regular option in single-select mode
            isOtherSelected: question.multiSelect ? current.isOtherSelected : false,
            otherText: question.multiSelect ? current.otherText : '',
          },
        }
      })
    },
    []
  )

  const handleOtherTextChange = useCallback((questionText: string, text: string) => {
    setAnswerStates((prev) => ({
      ...prev,
      [questionText]: {
        ...prev[questionText],
        otherText: text,
      },
    }))
  }, [])

  const isValid = useMemo(() => {
    return pendingQuestion.questions.every((question) => {
      const state = answerStates[question.question]
      if (!state) return false

      // Has at least one selected option or has "Other" with text
      const hasSelection = state.selectedOptions.length > 0
      const hasOtherAnswer = state.isOtherSelected && state.otherText.trim().length > 0

      return hasSelection || hasOtherAnswer
    })
  }, [pendingQuestion.questions, answerStates])

  const handleSubmit = useCallback(async () => {
    if (!isValid || isSubmitting) return

    const answers: Record<string, string> = {}

    for (const question of pendingQuestion.questions) {
      const state = answerStates[question.question]

      if (state.isOtherSelected && state.otherText.trim()) {
        // Include "Other" answer
        if (question.multiSelect && state.selectedOptions.length > 0) {
          // Combine selected options with Other
          answers[question.question] = [...state.selectedOptions, state.otherText.trim()].join(', ')
        } else {
          answers[question.question] = state.otherText.trim()
        }
      } else {
        // Just selected options
        answers[question.question] = state.selectedOptions.join(', ')
      }
    }

    await onSubmit(answers)
  }, [isValid, isSubmitting, pendingQuestion.questions, answerStates, onSubmit])

  return (
    <Card data-testid="question-panel" className="w-full">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <MessageCircleQuestion className="h-5 w-5" />
          Claude has a question for you
        </CardTitle>
        <CardDescription>Please answer the following to continue</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        {pendingQuestion.questions.map((question, qIndex) => (
          <QuestionItem
            key={`${question.question}-${qIndex}`}
            question={question}
            state={answerStates[question.question]}
            onOptionChange={(optionLabel, selected) =>
              handleOptionChange(question, optionLabel, selected)
            }
            onOtherTextChange={(text) => handleOtherTextChange(question.question, text)}
            disabled={isSubmitting}
          />
        ))}
      </CardContent>
      <CardFooter>
        <Button
          onClick={handleSubmit}
          disabled={!isValid || isSubmitting}
          className="w-full"
          data-testid="submit-answers-button"
        >
          {isSubmitting ? (
            <>
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              Submitting...
            </>
          ) : (
            'Submit Answers'
          )}
        </Button>
      </CardFooter>
    </Card>
  )
}

interface QuestionItemProps {
  question: UserQuestion
  state: QuestionAnswerState
  onOptionChange: (optionLabel: string, selected: boolean) => void
  onOtherTextChange: (text: string) => void
  disabled?: boolean
}

function QuestionItem({
  question,
  state,
  onOptionChange,
  onOtherTextChange,
  disabled = false,
}: QuestionItemProps) {
  const mode = question.multiSelect ? 'checkbox' : 'radio'
  const questionId = `question-${question.question.replace(/\s+/g, '-').toLowerCase()}`

  return (
    <div data-testid={`question-item-${question.header}`} className="flex flex-col gap-3">
      <div className="flex items-start gap-2">
        <Badge variant="secondary" className="shrink-0">
          {question.header}
        </Badge>
        <p className="text-sm font-medium">{question.question}</p>
      </div>
      <div className="flex flex-col gap-2 pl-2">
        {question.options.map((option) => (
          <QuestionOption
            key={option.label}
            option={option}
            questionId={questionId}
            isSelected={state.selectedOptions.includes(option.label)}
            onChange={(selected) => onOptionChange(option.label, selected)}
            mode={mode}
            disabled={disabled}
          />
        ))}
        {/* Other option */}
        <div
          data-testid="other-option"
          className={`flex cursor-pointer flex-col gap-2 rounded-lg border p-3 transition-colors ${
            state.isOtherSelected
              ? 'border-primary bg-primary/5'
              : 'border-border hover:border-primary/50 hover:bg-accent/50'
          } ${disabled ? 'pointer-events-none opacity-50' : ''}`}
          onClick={(e) => {
            // Don't toggle if clicking on the input field
            if (
              (e.target as HTMLElement).tagName === 'INPUT' &&
              (e.target as HTMLInputElement).type !== 'radio' &&
              (e.target as HTMLInputElement).type !== 'checkbox'
            )
              return
            if (disabled) return
            onOptionChange(OTHER_OPTION, !state.isOtherSelected)
          }}
        >
          <div className="flex items-center gap-3">
            <input
              id={`${questionId}-other`}
              type={mode}
              checked={state.isOtherSelected}
              onChange={() => onOptionChange(OTHER_OPTION, !state.isOtherSelected)}
              disabled={disabled}
              className="accent-primary h-4 w-4 shrink-0"
              data-testid="other-input"
            />
            <Label htmlFor={`${questionId}-other`} className="cursor-pointer font-medium">
              Other...
            </Label>
          </div>
          {state.isOtherSelected && (
            <Input
              data-testid="other-text-input"
              placeholder="Enter your answer..."
              value={state.otherText}
              onChange={(e) => onOtherTextChange(e.target.value)}
              onClick={(e) => e.stopPropagation()}
              disabled={disabled}
              className="mt-1"
              autoFocus
            />
          )}
        </div>
      </div>
      {question.multiSelect && (
        <p className="text-muted-foreground pl-2 text-xs">You can select multiple options</p>
      )}
    </div>
  )
}
