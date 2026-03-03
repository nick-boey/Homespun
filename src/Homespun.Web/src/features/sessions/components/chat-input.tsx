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
import {
  useChatInputStore,
  type PermissionMode,
  type ModelSelection,
} from '@/stores/chat-input-store'

export interface ChatInputProps {
  onSend: (message: string, permissionMode: PermissionMode, model: ModelSelection) => void
  disabled?: boolean
  isLoading?: boolean
  placeholder?: string
}

const PERMISSION_MODE_LABELS: Record<PermissionMode, string> = {
  default: 'Default',
  bypass: 'Bypass Permissions',
  'accept-edits': 'Accept Edits',
  plan: 'Plan Mode',
}

const MODEL_LABELS: Record<ModelSelection, string> = {
  opus: 'Opus',
  sonnet: 'Sonnet',
  haiku: 'Haiku',
}

export function ChatInput({
  onSend,
  disabled = false,
  isLoading = false,
  placeholder = 'Type a message...',
}: ChatInputProps) {
  const [value, setValue] = useState('')
  const { permissionMode, model, setPermissionMode, setModel } = useChatInputStore()

  const handleSubmit = useCallback(() => {
    const trimmedValue = value.trim()
    if (!trimmedValue || disabled) return

    onSend(trimmedValue, permissionMode, model)
    setValue('')
  }, [value, disabled, onSend, permissionMode, model])

  const handlePermissionModeChange = useCallback(
    (mode: PermissionMode) => {
      setPermissionMode(mode)
    },
    [setPermissionMode]
  )

  const handleModelChange = useCallback(
    (newModel: ModelSelection) => {
      setModel(newModel)
    },
    [setModel]
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
          {/* Permission Mode Selector */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="sm"
                className="gap-1"
                aria-label="Permission mode"
                disabled={disabled}
              >
                <Shield className="h-4 w-4" />
                <span className="hidden sm:inline">{PERMISSION_MODE_LABELS[permissionMode]}</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start">
              <DropdownMenuItem onClick={() => handlePermissionModeChange('default')}>
                Default
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => handlePermissionModeChange('bypass')}>
                Bypass Permissions
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => handlePermissionModeChange('accept-edits')}>
                Accept Edits
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => handlePermissionModeChange('plan')}>
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
                <span className="hidden sm:inline">{MODEL_LABELS[model]}</span>
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
