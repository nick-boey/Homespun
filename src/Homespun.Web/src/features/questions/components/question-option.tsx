import { cn } from '@/lib/utils'
import { Label } from '@/components/ui/label'
import type { QuestionOption as QuestionOptionType } from '@/types/signalr'

export interface QuestionOptionProps {
  option: QuestionOptionType
  questionId: string
  isSelected: boolean
  onChange: (selected: boolean) => void
  mode: 'radio' | 'checkbox'
  disabled?: boolean
}

export function QuestionOption({
  option,
  questionId,
  isSelected,
  onChange,
  mode,
  disabled = false,
}: QuestionOptionProps) {
  const inputId = `${questionId}-${option.label}`
  const inputType = mode === 'radio' ? 'radio' : 'checkbox'

  const handleClick = (e: React.MouseEvent) => {
    // Prevent double-firing when clicking on the input itself
    if ((e.target as HTMLElement).tagName === 'INPUT') return
    if (disabled) return
    // For radio buttons, always select (don't toggle off)
    // For checkboxes, toggle
    if (mode === 'radio') {
      onChange(true)
    } else {
      onChange(!isSelected)
    }
  }

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (mode === 'radio') {
      onChange(true)
    } else {
      onChange(e.target.checked)
    }
  }

  return (
    <div
      data-testid={`question-option-${option.label}`}
      className={cn(
        'flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors',
        isSelected
          ? 'border-primary bg-primary/5'
          : 'border-border hover:border-primary/50 hover:bg-accent/50',
        disabled && 'pointer-events-none opacity-50'
      )}
      onClick={handleClick}
    >
      <input
        id={inputId}
        type={inputType}
        name={mode === 'radio' ? questionId : undefined}
        checked={isSelected}
        onChange={handleInputChange}
        disabled={disabled}
        className="accent-primary mt-0.5 h-4 w-4 shrink-0"
        data-testid={`option-input-${option.label}`}
      />
      <Label htmlFor={inputId} className="flex cursor-pointer flex-col gap-1 font-normal">
        <span className="font-medium">{option.label}</span>
        {option.description && (
          <span className="text-muted-foreground text-sm">{option.description}</span>
        )}
      </Label>
    </div>
  )
}
