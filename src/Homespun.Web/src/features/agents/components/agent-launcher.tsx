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
import { SessionMode } from '@/api'
import type { ClaudeSession } from '@/api/generated/types.gen'

const MODELS = [
  { value: 'opus', label: 'Opus' },
  { value: 'sonnet', label: 'Sonnet' },
  { value: 'haiku', label: 'Haiku' },
] as const

const DEFAULT_MODEL = 'opus'
const MODEL_STORAGE_KEY = 'agent-launcher-model'
const PROMPT_STORAGE_KEY = 'agent-launcher-prompt'

/** Special "None" option - starts session in Plan mode without a system prompt */
const NO_PROMPT_NAME = '__none__'
const NO_PROMPT_OPTION = {
  name: NO_PROMPT_NAME,
  displayName: 'None - Start without prompt (Plan mode)',
  initialMessage: undefined,
  mode: SessionMode.PLAN,
} as const

// TODO: Join this with the agent-launcher-dialog

/** Get the initial prompt name from localStorage or return "None" as default */
function getInitialPromptName(): string {
  return localStorage.getItem(PROMPT_STORAGE_KEY) ?? NO_PROMPT_NAME
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
  const [selectedPromptName, setSelectedPromptName] = useState<string>(getInitialPromptName)

  // Update localStorage when selections change
  useEffect(() => {
    localStorage.setItem(MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  useEffect(() => {
    if (selectedPromptName) {
      localStorage.setItem(PROMPT_STORAGE_KEY, selectedPromptName)
    }
  }, [selectedPromptName])

  // Compute the effective selected prompt name
  // "No prompt" is always valid; otherwise check if selected prompt exists in list
  const effectivePromptName = useMemo(() => {
    // If "No prompt" is selected, use it
    if (selectedPromptName === NO_PROMPT_NAME) {
      return NO_PROMPT_NAME
    }
    // Check if the selected prompt exists in the loaded prompts
    if (prompts && prompts.length > 0) {
      const selectedExists = prompts.some((p) => p.name === selectedPromptName)
      if (selectedExists) {
        return selectedPromptName
      }
    }
    // Default to "No prompt"
    return NO_PROMPT_NAME
  }, [prompts, selectedPromptName])

  // Handler for prompt selection that updates state
  const handlePromptChange = (value: string) => {
    setSelectedPromptName(value)
  }

  // Get the selected prompt object (or NO_PROMPT_OPTION if "No prompt" selected)
  const selectedPrompt =
    effectivePromptName === NO_PROMPT_NAME
      ? NO_PROMPT_OPTION
      : prompts?.find((p) => p.name === effectivePromptName)

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
      if (effectivePromptName === NO_PROMPT_NAME && session.id) {
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
        <Select value={effectivePromptName} onValueChange={handlePromptChange} disabled={isLoading}>
          <SelectTrigger className="w-40" aria-label="Select prompt">
            <SelectValue placeholder="Select prompt" />
          </SelectTrigger>
          <SelectContent>
            {/* "None" option always first */}
            <SelectItem key={NO_PROMPT_NAME} value={NO_PROMPT_NAME}>
              {NO_PROMPT_OPTION.displayName}
            </SelectItem>
            {prompts?.map((prompt) => (
              <SelectItem key={prompt.name} value={prompt.name ?? ''}>
                {prompt.name}
                {prompt.isOverride && ' (project)'}
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
