/**
 * InlineIssueEditor - Input component for inline issue creation/editing.
 *
 * Appears in-place within the task graph when creating or editing an issue.
 * Supports Vim-like keyboard commands for hierarchy changes.
 */

import { memo, useEffect, useRef, type KeyboardEvent, type ChangeEvent } from 'react'
import { cn } from '@/lib/utils'
import { Check, Pencil, X } from 'lucide-react'
import { EditCursorPosition } from '../types'

export interface InlineIssueEditorProps {
  /** Current title text */
  title: string
  /** Called when title changes */
  onTitleChange: (title: string) => void
  /** Called when user saves (Enter or OK button) */
  onSave: () => void
  /** Called when user saves and wants to edit description (Shift+Enter or OK+Edit button) */
  onSaveAndEdit: () => void
  /** Called when user cancels (Escape or Cancel button) */
  onCancel: () => void
  /** Called when user indents (Tab - make new issue parent of reference) */
  onIndent: () => void
  /** Called when user unindents (Shift+Tab - make new issue child of reference) */
  onUnindent: () => void
  /** Placeholder text for empty input */
  placeholder?: string
  /** Where to position the cursor on focus */
  cursorPosition?: EditCursorPosition
  /** Whether to show "Parent of" indicator */
  showParentIndicator?: boolean
  /** Whether to show "Child of" indicator */
  showChildIndicator?: boolean
  /** Whether the editor is above the reference issue */
  isAbove?: boolean
  /** Additional CSS classes */
  className?: string
}

/**
 * Inline editor for creating/editing issues within the task graph.
 */
export const InlineIssueEditor = memo(function InlineIssueEditor({
  title,
  onTitleChange,
  onSave,
  onSaveAndEdit,
  onCancel,
  onIndent,
  onUnindent,
  placeholder = 'Enter issue title...',
  cursorPosition = EditCursorPosition.End,
  showParentIndicator = false,
  showChildIndicator = false,
  isAbove = false,
  className,
}: InlineIssueEditorProps) {
  const inputRef = useRef<HTMLInputElement>(null)

  // Auto-focus and position cursor on mount
  useEffect(() => {
    const input = inputRef.current
    if (!input) return

    input.focus()

    // Position cursor based on cursorPosition
    const len = input.value.length
    switch (cursorPosition) {
      case EditCursorPosition.Start:
        input.setSelectionRange(0, 0)
        break
      case EditCursorPosition.End:
        input.setSelectionRange(len, len)
        break
      case EditCursorPosition.Replace:
        input.setSelectionRange(0, len)
        break
    }
  }, [cursorPosition])

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    switch (e.key) {
      case 'Escape':
        e.preventDefault()
        onCancel()
        break
      case 'Enter':
        e.preventDefault()
        if (e.shiftKey) {
          onSaveAndEdit()
        } else {
          onSave()
        }
        break
      case 'Tab':
        e.preventDefault()
        if (e.shiftKey) {
          onUnindent()
        } else {
          onIndent()
        }
        break
    }
  }

  const handleChange = (e: ChangeEvent<HTMLInputElement>) => {
    onTitleChange(e.target.value)
  }

  // Determine indicator text
  const getIndicatorText = () => {
    if (showParentIndicator) {
      return isAbove ? 'Parent of below' : 'Parent of above'
    }
    if (showChildIndicator) {
      return isAbove ? 'Child of below' : 'Child of above'
    }
    return null
  }

  const indicatorText = getIndicatorText()
  const indicatorClass = showParentIndicator ? 'parent' : showChildIndicator ? 'child' : ''

  return (
    <div data-testid="inline-issue-create" className={cn('flex flex-1 items-center gap-1', className)}>
      <input
        ref={inputRef}
        type="text"
        data-testid="inline-issue-input"
        className={cn(
          'flex-1 rounded border-2 bg-background px-2 py-1 text-sm outline-none',
          'border-primary/50 shadow-[0_0_8px_rgba(var(--primary)/0.3)]',
          'focus:border-primary focus:shadow-[0_0_12px_rgba(var(--primary)/0.5)]',
          'placeholder:text-muted-foreground'
        )}
        value={title}
        onChange={handleChange}
        onKeyDown={handleKeyDown}
        placeholder={placeholder}
      />

      <div className="flex shrink-0 items-center gap-0.5">
        {/* Save button */}
        <button
          type="button"
          data-testid="inline-ok-btn"
          title="Save (Enter)"
          onClick={onSave}
          className={cn(
            'flex h-7 w-7 items-center justify-center rounded',
            'bg-primary text-primary-foreground',
            'hover:bg-primary/90 hover:scale-105',
            'transition-transform'
          )}
        >
          <Check className="h-3.5 w-3.5" />
        </button>

        {/* Save and edit description button */}
        <button
          type="button"
          data-testid="inline-ok-edit-btn"
          title="Save and edit description (Shift+Enter)"
          onClick={onSaveAndEdit}
          className={cn(
            'flex h-7 w-7 items-center justify-center rounded',
            'bg-green-600 text-white',
            'hover:bg-green-700 hover:scale-105',
            'transition-transform'
          )}
        >
          <Pencil className="h-3.5 w-3.5" />
        </button>

        {/* Cancel button */}
        <button
          type="button"
          data-testid="inline-cancel-btn"
          title="Cancel (Escape)"
          onClick={onCancel}
          className={cn(
            'flex h-7 w-7 items-center justify-center rounded',
            'bg-muted text-muted-foreground',
            'hover:bg-destructive hover:text-destructive-foreground hover:scale-105',
            'transition-transform'
          )}
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* Hierarchy indicator */}
      {indicatorText && (
        <span
          className={cn(
            'lane-indicator ml-2 shrink-0 rounded px-2 py-0.5 text-xs font-medium',
            indicatorClass,
            showParentIndicator && 'bg-blue-500/20 text-blue-600 dark:text-blue-400',
            showChildIndicator && 'bg-amber-500/20 text-amber-600 dark:text-amber-400'
          )}
        >
          {indicatorText}
        </span>
      )}
    </div>
  )
})

export { InlineIssueEditor as default }
