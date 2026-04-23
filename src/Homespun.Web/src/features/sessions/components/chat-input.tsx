import { useState, useCallback, useRef, useEffect } from 'react'
import { Send, Loader2, Shield, Sparkles, Hammer } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'
import type { ModelSelection } from '@/stores/session-settings-store'
import type { SessionMode } from '@/types/signalr'
import {
  useMentionTrigger,
  useProjectFiles,
  useSearchablePrs,
  MentionSearchPopup,
  type MentionSelection,
} from '@/features/search'

export interface ChatInputProps {
  onSend: (message: string, sessionMode: SessionMode, model: ModelSelection) => void
  sessionMode: SessionMode
  sessionModel: ModelSelection
  onModeChange: (mode: SessionMode) => void
  onModelChange: (model: ModelSelection) => void
  projectId?: string
  disabled?: boolean
  isLoading?: boolean
  placeholder?: string
}

const MODEL_LABELS: Record<ModelSelection, string> = {
  opus: 'Opus',
  sonnet: 'Sonnet',
  haiku: 'Haiku',
}

export function ChatInput({
  onSend,
  sessionMode,
  sessionModel,
  onModeChange,
  onModelChange,
  projectId = '',
  disabled = false,
  isLoading = false,
  placeholder = 'Type a message...',
}: ChatInputProps) {
  const [value, setValue] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const handleSubmit = useCallback(
    (event?: React.FormEvent) => {
      event?.preventDefault()
      const trimmed = value.trim()
      if (!trimmed || disabled) return
      onSend(trimmed, sessionMode, sessionModel)
      setValue('')
    },
    [value, disabled, onSend, sessionMode, sessionModel]
  )

  const toggleSessionMode = useCallback(() => {
    const nextMode: SessionMode = sessionMode === 'build' ? 'plan' : 'build'
    onModeChange(nextMode)
  }, [sessionMode, onModeChange])

  const handleTextareaKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Tab' && e.shiftKey) {
        e.preventDefault()
        toggleSessionMode()
      }
    },
    [toggleSessionMode]
  )

  return (
    <form
      onSubmit={handleSubmit}
      className="bg-background border-input focus-within:border-ring focus-within:ring-ring/50 flex w-full flex-col gap-2 rounded-2xl border p-2 shadow-xs focus-within:ring-[3px]"
    >
      <div className="flex items-center justify-between px-1">
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={toggleSessionMode}
            disabled={disabled}
            className="gap-1"
            aria-label="Toggle session mode"
          >
            {sessionMode === 'build' ? (
              <>
                <Hammer className="h-4 w-4" />
                <span>Build</span>
              </>
            ) : (
              <>
                <Shield className="h-4 w-4" />
                <span>Plan</span>
              </>
            )}
          </Button>

          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="gap-1"
                aria-label="Model selection"
                disabled={disabled}
              >
                <Sparkles className="h-4 w-4" />
                <span>{MODEL_LABELS[sessionModel]}</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start">
              <DropdownMenuItem onClick={() => onModelChange('opus')}>Opus</DropdownMenuItem>
              <DropdownMenuItem onClick={() => onModelChange('sonnet')}>Sonnet</DropdownMenuItem>
              <DropdownMenuItem onClick={() => onModelChange('haiku')}>Haiku</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>

      <ComposerTextareaWithSearch
        projectId={projectId}
        value={value}
        setValue={setValue}
        placeholder={placeholder}
        disabled={disabled}
        onKeyDown={handleTextareaKeyDown}
        textareaRef={textareaRef}
      />

      <div className="flex items-center justify-end px-1 pb-1">
        <Button
          type="submit"
          size="icon"
          variant="ghost"
          disabled={disabled || !value.trim()}
          aria-label="Send message"
        >
          {isLoading ? (
            <Loader2 className="h-4 w-4 animate-spin" data-testid="send-loading" />
          ) : (
            <Send className="h-4 w-4" />
          )}
        </Button>
      </div>
    </form>
  )
}

interface ComposerTextareaWithSearchProps {
  projectId: string
  value: string
  setValue: (value: string) => void
  placeholder: string
  disabled: boolean
  onKeyDown: (e: React.KeyboardEvent<HTMLTextAreaElement>) => void
  textareaRef: React.RefObject<HTMLTextAreaElement | null>
}

function ComposerTextareaWithSearch({
  projectId,
  value,
  setValue,
  placeholder,
  disabled,
  onKeyDown,
  textareaRef,
}: ComposerTextareaWithSearchProps) {
  const [cursorPosition, setCursorPosition] = useState(0)
  const [isSearchHidden, setIsSearchHidden] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  const { files, isLoading: isLoadingFiles } = useProjectFiles(projectId)
  const { prs, isLoading: isLoadingPrs } = useSearchablePrs(projectId)

  const triggerState = useMentionTrigger(value, cursorPosition)
  const isSearchOpen = triggerState.active && !isSearchHidden

  const prevTriggerPos = useRef(-1)
  useEffect(() => {
    if (triggerState.triggerPosition !== prevTriggerPos.current && triggerState.active) {
      setIsSearchHidden(false)
    }
    prevTriggerPos.current = triggerState.triggerPosition
  }, [triggerState.triggerPosition, triggerState.active])

  const handleCursorChange = useCallback(() => {
    if (textareaRef.current) {
      setCursorPosition(textareaRef.current.selectionStart ?? 0)
    }
  }, [textareaRef])

  useEffect(() => {
    setCursorPosition(value.length)
  }, [value])

  const handleSelect = useCallback(
    (selection: MentionSelection) => {
      if (!triggerState.active) return

      const { triggerPosition, query } = triggerState
      const beforeTrigger = value.slice(0, triggerPosition)
      const afterQuery = value.slice(triggerPosition + 1 + query.length)

      let insertion: string
      if (selection.type === '@') {
        const hasSpaces = selection.value.includes(' ')
        insertion = hasSpaces ? `@"${selection.value}"` : `@${selection.value}`
      } else {
        insertion = `PR #${selection.value}`
      }

      const newValue = beforeTrigger + insertion + afterQuery
      setValue(newValue)

      const newCursorPos = triggerPosition + insertion.length
      requestAnimationFrame(() => {
        if (textareaRef.current) {
          textareaRef.current.setSelectionRange(newCursorPos, newCursorPos)
          textareaRef.current.focus()
          setCursorPosition(newCursorPos)
        }
      })
      setIsSearchHidden(true)
    },
    [triggerState, value, setValue, textareaRef]
  )

  const handleCloseSearch = useCallback(() => {
    setIsSearchHidden(true)
  }, [])

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (isSearchOpen && e.key === 'Escape') {
        e.preventDefault()
        handleCloseSearch()
        return
      }
      if (isSearchOpen && e.key === 'Enter') {
        return
      }
      // Enter without Shift submits; let the form handle it.
      if (e.key === 'Enter' && !e.shiftKey && !isSearchOpen) {
        e.preventDefault()
        const form = e.currentTarget.form
        form?.requestSubmit()
        return
      }
      onKeyDown(e)
    },
    [isSearchOpen, onKeyDown, handleCloseSearch]
  )

  return (
    <div ref={containerRef} className="relative">
      <textarea
        ref={textareaRef}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        onKeyDown={handleKeyDown}
        onSelect={handleCursorChange}
        onClick={handleCursorChange}
        onKeyUp={handleCursorChange}
        rows={1}
        className={cn(
          'placeholder:text-muted-foreground max-h-60 min-h-[44px] w-full resize-none bg-transparent px-2 py-2 text-sm leading-6 outline-none',
          disabled && 'cursor-not-allowed opacity-60'
        )}
      />
      {projectId && (
        <MentionSearchPopup
          open={isSearchOpen}
          triggerType={triggerState.type ?? '@'}
          query={triggerState.query}
          files={files ?? []}
          prs={prs ?? []}
          onSelect={handleSelect}
          onClose={handleCloseSearch}
          isLoadingFiles={isLoadingFiles}
          isLoadingPrs={isLoadingPrs}
          className="bottom-full left-0 mb-1"
        />
      )}
    </div>
  )
}
