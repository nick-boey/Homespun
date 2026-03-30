import { useState, useCallback, useRef, useEffect } from 'react'
import { Send, Loader2, Shield, Sparkles, Hammer, ScrollText } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  DropdownMenuSeparator,
} from '@/components/ui/dropdown-menu'
import {
  PromptInput,
  PromptInputTextarea,
  PromptInputActions,
  PromptInputAction,
  usePromptInput,
} from '@/components/ui/prompt-input'
import type { ModelSelection } from '@/stores/session-settings-store'
import type { SessionMode } from '@/types/signalr'
import {
  useMentionTrigger,
  useProjectFiles,
  useSearchablePrs,
  MentionSearchPopup,
  type MentionSelection,
} from '@/features/search'
import { useAgentPrompts } from '@/features/agents/hooks'
import { renderPromptTemplate, type PromptContext } from '../utils/render-prompt-template'

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
  issueContext?: PromptContext | null
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
  issueContext,
}: ChatInputProps) {
  const [value, setValue] = useState('')

  // Fetch available prompts for the project
  const { data: prompts, isLoading: promptsLoading } = useAgentPrompts(projectId || '')

  const handleSubmit = useCallback(() => {
    const trimmedValue = value.trim()
    if (!trimmedValue || disabled) return

    onSend(trimmedValue, sessionMode, sessionModel)
    setValue('')
  }, [value, disabled, onSend, sessionMode, sessionModel])

  const toggleSessionMode = useCallback(() => {
    const newMode = sessionMode === 'build' ? 'plan' : 'build'
    onModeChange(newMode)
  }, [sessionMode, onModeChange])

  const handleModelChange = useCallback(
    (newModel: ModelSelection) => {
      onModelChange(newModel)
    },
    [onModelChange]
  )

  // Handle prompt selection - appends rendered template to input
  const handlePromptSelect = useCallback(
    (promptName: string) => {
      if (!prompts || !issueContext) return

      const prompt = prompts.find((p) => p.name === promptName)
      if (!prompt?.initialMessage) return

      const rendered = renderPromptTemplate(prompt.initialMessage, issueContext)
      if (!rendered) return

      // Append to existing value with blank line separator
      setValue((prev) => (prev ? `${prev}\n\n${rendered}` : rendered))
    },
    [prompts, issueContext]
  )

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
    <PromptInput
      value={value}
      onValueChange={setValue}
      onSubmit={handleSubmit}
      disabled={disabled}
      isLoading={isLoading}
      className="w-full"
    >
      {/* Controls above textarea */}
      <PromptInputActions className="justify-between px-2 pt-1">
        <div className="flex items-center gap-2">
          {/* Session Mode Toggle */}
          <Button
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

          {/* Model Selector */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
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
              <DropdownMenuItem onClick={() => handleModelChange('opus')}>Opus</DropdownMenuItem>
              <DropdownMenuItem onClick={() => handleModelChange('sonnet')}>
                Sonnet
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => handleModelChange('haiku')}>Haiku</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>

          {/* Prompt Selector - only show when projectId and issueContext are available */}
          {projectId && issueContext && (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="sm"
                  className="gap-1"
                  aria-label="Select prompt"
                  disabled={disabled || promptsLoading}
                >
                  <ScrollText className="h-4 w-4" />
                  <span>Prompt</span>
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="start">
                <DropdownMenuItem disabled>
                  <span className="text-muted-foreground">Select a prompt to fill</span>
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                {prompts?.map((prompt) => (
                  <DropdownMenuItem
                    key={prompt.name}
                    onClick={() => handlePromptSelect(prompt.name ?? '')}
                  >
                    {prompt.name}
                    {prompt.isOverride && ' (project)'}
                  </DropdownMenuItem>
                ))}
                {(!prompts || prompts.length === 0) && (
                  <DropdownMenuItem disabled>
                    <span className="text-muted-foreground">No prompts available</span>
                  </DropdownMenuItem>
                )}
              </DropdownMenuContent>
            </DropdownMenu>
          )}
        </div>
      </PromptInputActions>

      {/* Textarea with search popup */}
      <ChatInputTextareaWithSearch
        projectId={projectId}
        value={value}
        setValue={setValue}
        placeholder={placeholder}
        onKeyDown={handleTextareaKeyDown}
      />

      {/* Send button below textarea */}
      <PromptInputActions className="justify-end px-2 pb-2">
        <PromptInputAction tooltip="Send message (Enter)">
          <Button
            size="icon"
            variant="ghost"
            onClick={handleSubmit}
            disabled={disabled || !value.trim()}
            aria-label="Send message"
          >
            {isLoading ? (
              <Loader2 className="h-4 w-4 animate-spin" data-testid="send-loading" />
            ) : (
              <Send className="h-4 w-4" />
            )}
          </Button>
        </PromptInputAction>
      </PromptInputActions>
    </PromptInput>
  )
}

/**
 * Internal component that wraps the textarea with mention search.
 * Uses the PromptInput context to access the textarea ref.
 */
function ChatInputTextareaWithSearch({
  projectId,
  value,
  setValue,
  placeholder,
  onKeyDown,
}: {
  projectId: string
  value: string
  setValue: (value: string) => void
  placeholder: string
  onKeyDown: (e: React.KeyboardEvent<HTMLTextAreaElement>) => void
}) {
  const { textareaRef } = usePromptInput()
  const [cursorPosition, setCursorPosition] = useState(0)
  const [isSearchHidden, setIsSearchHidden] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  // Fetch data for search
  const { files, isLoading: isLoadingFiles } = useProjectFiles(projectId)
  const { prs, isLoading: isLoadingPrs } = useSearchablePrs(projectId)

  // Detect trigger
  const triggerState = useMentionTrigger(value, cursorPosition)

  // Show popup when trigger is active
  const isSearchOpen = triggerState.active && !isSearchHidden

  // Reset hidden state when trigger position changes
  // This is a valid pattern for resetting state based on derived values
  const prevTriggerPos = useRef(-1)
  useEffect(() => {
    if (triggerState.triggerPosition !== prevTriggerPos.current && triggerState.active) {
      setIsSearchHidden(false)
    }
    prevTriggerPos.current = triggerState.triggerPosition
  }, [triggerState.triggerPosition, triggerState.active])

  // Track cursor position
  const handleCursorChange = useCallback(() => {
    if (textareaRef.current) {
      setCursorPosition(textareaRef.current.selectionStart ?? 0)
    }
  }, [textareaRef])

  // Also update cursor position when value changes (for programmatic updates like fill())
  // Set cursor to end of value when it changes externally
  useEffect(() => {
    setCursorPosition(value.length)
  }, [value])

  // Handle selection from popup
  const handleSelect = useCallback(
    (selection: MentionSelection) => {
      if (!triggerState.active) return

      const { triggerPosition, query } = triggerState
      const beforeTrigger = value.slice(0, triggerPosition)
      const afterQuery = value.slice(triggerPosition + 1 + query.length)

      // Format the insertion based on type
      let insertion: string
      if (selection.type === '@') {
        // Use quotes only if the file path contains spaces
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

  // Close popup
  const handleCloseSearch = useCallback(() => {
    setIsSearchHidden(true)
  }, [])

  // Handle keyboard events for search
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      // If search is open, let the popup handle navigation keys
      if (isSearchOpen && (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Escape')) {
        // The popup handles these via document event listener
        if (e.key === 'Escape') {
          e.preventDefault()
          handleCloseSearch()
          return
        }
      }

      // If search is open and Enter is pressed, don't submit the form
      if (isSearchOpen && e.key === 'Enter') {
        // Let the popup handle Enter for selection
        return
      }

      onKeyDown(e)
    },
    [isSearchOpen, onKeyDown, handleCloseSearch]
  )

  return (
    <div ref={containerRef} className="relative">
      <PromptInputTextarea
        placeholder={placeholder}
        onKeyDown={handleKeyDown}
        onSelect={handleCursorChange}
        onClick={handleCursorChange}
        onKeyUp={handleCursorChange}
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
