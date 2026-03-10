import { useState, useCallback } from 'react'
import { Send, Loader2, Shield, Sparkles } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
  PromptInput,
  PromptInputTextarea,
  PromptInputActions,
  PromptInputAction,
} from '@/components/ui/prompt-input'
import type { ModelSelection } from '@/stores/session-settings-store'
import type { SessionMode } from '@/types/signalr'

export interface ChatInputProps {
  onSend: (message: string, sessionMode: SessionMode, model: ModelSelection) => void
  sessionMode: SessionMode
  sessionModel: ModelSelection
  onModeChange: (mode: SessionMode) => void
  onModelChange: (model: ModelSelection) => void
  disabled?: boolean
  isLoading?: boolean
  placeholder?: string
}

const SESSION_MODE_LABELS: Record<SessionMode, string> = {
  Build: 'Build Mode',
  Plan: 'Plan Mode',
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
  disabled = false,
  isLoading = false,
  placeholder = 'Type a message...',
}: ChatInputProps) {
  const [value, setValue] = useState('')

  const handleSubmit = useCallback(() => {
    const trimmedValue = value.trim()
    if (!trimmedValue || disabled) return

    onSend(trimmedValue, sessionMode, sessionModel)
    setValue('')
  }, [value, disabled, onSend, sessionMode, sessionModel])

  const handleSessionModeChange = useCallback(
    (mode: SessionMode) => {
      onModeChange(mode)
    },
    [onModeChange]
  )

  const handleModelChange = useCallback(
    (newModel: ModelSelection) => {
      onModelChange(newModel)
    },
    [onModelChange]
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
      <PromptInputTextarea placeholder={placeholder} />
      <PromptInputActions className="justify-between px-2 pb-2">
        <div className="flex items-center gap-2">
          {/* Session Mode Selector */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="sm"
                className="gap-1"
                aria-label="Session mode"
                disabled={disabled}
              >
                <Shield className="h-4 w-4" />
                <span className="hidden sm:inline">{SESSION_MODE_LABELS[sessionMode]}</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start">
              <DropdownMenuItem onClick={() => handleSessionModeChange('Build')}>
                Build Mode
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => handleSessionModeChange('Plan')}>
                Plan Mode
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>

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
                <span className="hidden sm:inline">{MODEL_LABELS[sessionModel]}</span>
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
        </div>

        {/* Send Button */}
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
