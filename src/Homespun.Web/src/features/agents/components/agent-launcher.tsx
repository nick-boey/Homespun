import { useState, useEffect, useMemo } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { Play } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Loader } from '@/components/ui/loader'
import { useStartAgent, useAgentPrompts } from '../hooks'
import type { ClaudeSession, SessionMode } from '@/api/generated/types.gen'

const MODELS = [
  { value: 'claude-sonnet-4-20250514', label: 'Sonnet' },
  { value: 'claude-opus-4-20250514', label: 'Opus' },
  { value: 'claude-haiku-3-5-20241022', label: 'Haiku' },
] as const

const DEFAULT_MODEL = 'claude-opus-4-20250514'
const MODEL_STORAGE_KEY = 'agent-launcher-model'
const PROMPT_STORAGE_KEY = 'agent-launcher-prompt'

/** Special "None" option - starts session in Plan mode without a system prompt */
const NO_PROMPT_ID = '__none__'
const NO_PROMPT_OPTION = {
  id: NO_PROMPT_ID,
  name: 'None - Start without prompt (Plan mode)',
  initialMessage: undefined,
  mode: 0, // SessionMode.Plan
} as const

/** Get the initial prompt ID from localStorage or return "None" as default */
function getInitialPromptId(): string {
  return localStorage.getItem(PROMPT_STORAGE_KEY) ?? NO_PROMPT_ID
}

interface AgentLauncherProps {
  projectId: string
  entityId: string
  workingDirectory: string
  onStart?: (session: ClaudeSession) => void
  onError?: (error: Error) => void
  className?: string
}

/**
 * Component for starting an agent session on an issue or PR.
 * Provides prompt and model selection with a start button.
 */
export function AgentLauncher({
  projectId,
  entityId,
  workingDirectory,
  onStart,
  onError,
  className,
}: AgentLauncherProps) {
  const navigate = useNavigate()
  const { data: prompts, isLoading: promptsLoading } = useAgentPrompts(projectId)
  const startAgent = useStartAgent()

  // Load persisted selections from localStorage
  const [selectedModel, setSelectedModel] = useState<string>(() => {
    return localStorage.getItem(MODEL_STORAGE_KEY) ?? DEFAULT_MODEL
  })
  const [selectedPromptId, setSelectedPromptId] = useState<string>(getInitialPromptId)

  // Update localStorage when selections change
  useEffect(() => {
    localStorage.setItem(MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  useEffect(() => {
    if (selectedPromptId) {
      localStorage.setItem(PROMPT_STORAGE_KEY, selectedPromptId)
    }
  }, [selectedPromptId])

  // Compute the effective selected prompt ID
  // "No prompt" is always valid; otherwise check if selected prompt exists in list
  const effectivePromptId = useMemo(() => {
    // If "No prompt" is selected, use it
    if (selectedPromptId === NO_PROMPT_ID) {
      return NO_PROMPT_ID
    }
    // Check if the selected prompt exists in the loaded prompts
    if (prompts && prompts.length > 0) {
      const selectedExists = prompts.some((p) => p.id === selectedPromptId)
      if (selectedExists) {
        return selectedPromptId
      }
    }
    // Default to "No prompt"
    return NO_PROMPT_ID
  }, [prompts, selectedPromptId])

  // Handler for prompt selection that updates state
  const handlePromptChange = (value: string) => {
    setSelectedPromptId(value)
  }

  // Get the selected prompt object (or NO_PROMPT_OPTION if "No prompt" selected)
  const selectedPrompt =
    effectivePromptId === NO_PROMPT_ID
      ? NO_PROMPT_OPTION
      : prompts?.find((p) => p.id === effectivePromptId)

  const handleStart = async () => {
    try {
      const session = await startAgent.mutateAsync({
        entityId,
        projectId,
        workingDirectory,
        model: selectedModel,
        mode: selectedPrompt?.mode as SessionMode | undefined,
        systemPrompt: selectedPrompt?.initialMessage ?? undefined,
      })

      // Navigate to session page if "None" was selected
      if (effectivePromptId === NO_PROMPT_ID && session.id) {
        navigate({ to: '/sessions/$sessionId', params: { sessionId: session.id } })
      }

      onStart?.(session)
    } catch (error) {
      onError?.(error as Error)
    }
  }

  const isLoading = promptsLoading || startAgent.isPending

  return (
    <div className={className}>
      <div className="flex items-center gap-2">
        {/* Prompt selector */}
        <Select value={effectivePromptId} onValueChange={handlePromptChange} disabled={isLoading}>
          <SelectTrigger className="w-40" aria-label="Select prompt">
            <SelectValue placeholder="Select prompt" />
          </SelectTrigger>
          <SelectContent>
            {/* "None" option always first */}
            <SelectItem key={NO_PROMPT_ID} value={NO_PROMPT_ID}>
              {NO_PROMPT_OPTION.name}
            </SelectItem>
            {prompts?.map((prompt) => (
              <SelectItem key={prompt.id} value={prompt.id ?? ''}>
                {prompt.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        {/* Model selector */}
        <Select value={selectedModel} onValueChange={setSelectedModel} disabled={isLoading}>
          <SelectTrigger className="w-24" aria-label="Select model">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {MODELS.map((model) => (
              <SelectItem key={model.value} value={model.value}>
                {model.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        {/* Start button */}
        <Button size="sm" onClick={handleStart} disabled={isLoading} className="gap-1.5">
          {startAgent.isPending ? (
            <Loader variant="circular" size="sm" />
          ) : (
            <Play className="h-3.5 w-3.5" />
          )}
          Start Agent
        </Button>
      </div>
    </div>
  )
}
