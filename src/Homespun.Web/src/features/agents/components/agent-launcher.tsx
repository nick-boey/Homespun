import { useState, useEffect, useMemo } from 'react'
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

const MODEL_STORAGE_KEY = 'agent-launcher-model'
const PROMPT_STORAGE_KEY = 'agent-launcher-prompt'

/** Get the initial prompt ID from localStorage or return empty string */
function getInitialPromptId(): string {
  return localStorage.getItem(PROMPT_STORAGE_KEY) ?? ''
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
  const { data: prompts, isLoading: promptsLoading } = useAgentPrompts(projectId)
  const startAgent = useStartAgent()

  // Load persisted selections from localStorage
  const [selectedModel, setSelectedModel] = useState<string>(() => {
    return localStorage.getItem(MODEL_STORAGE_KEY) ?? MODELS[0].value
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
  // If the selected prompt exists in the list, use it; otherwise use first available
  const effectivePromptId = useMemo(() => {
    if (!prompts || prompts.length === 0) {
      return ''
    }
    // Check if the selected prompt exists in the loaded prompts
    const selectedExists = prompts.some((p) => p.id === selectedPromptId)
    if (selectedExists) {
      return selectedPromptId
    }
    // Fall back to the first prompt
    return prompts[0].id ?? ''
  }, [prompts, selectedPromptId])

  // Handler for prompt selection that updates state
  const handlePromptChange = (value: string) => {
    setSelectedPromptId(value)
  }

  const selectedPrompt = prompts?.find((p) => p.id === effectivePromptId)

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
        <Select
          value={effectivePromptId}
          onValueChange={handlePromptChange}
          disabled={isLoading || !prompts?.length}
        >
          <SelectTrigger className="w-40" aria-label="Select prompt">
            <SelectValue placeholder="Select prompt" />
          </SelectTrigger>
          <SelectContent>
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
        <Button
          size="sm"
          onClick={handleStart}
          disabled={isLoading || !effectivePromptId}
          className="gap-1.5"
        >
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
