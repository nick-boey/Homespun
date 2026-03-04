import { useState, useEffect, useMemo, useCallback } from 'react'
import { Play } from 'lucide-react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Loader } from '@/components/ui/loader'
import { AlertCircle } from 'lucide-react'
import { useEnsureClone } from '../hooks/use-ensure-clone'
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

interface AgentLauncherDialogProps {
  /** Whether the dialog is open */
  open: boolean
  /** Callback when open state changes */
  onOpenChange: (open: boolean) => void
  /** The project ID */
  projectId: string
  /** The issue ID to run the agent on */
  issueId: string
  /** Callback when a session is started */
  onSessionStart?: (session: ClaudeSession) => void
  /** Callback when there's an error starting the session */
  onError?: (error: Error) => void
}

/**
 * Dialog component for launching an agent on an issue.
 *
 * This component:
 * 1. Resolves the branch name for the issue
 * 2. Shows prompt/model selection
 * 3. Creates clone when start is clicked
 * 4. Starts agent session with the clone path
 */
export function AgentLauncherDialog({
  open,
  onOpenChange,
  projectId,
  issueId,
  onSessionStart,
  onError,
}: AgentLauncherDialogProps) {
  // Clone management
  const {
    branchName,
    isLoading: isLoadingClone,
    isCreating,
    isError,
    error,
    ensureClone,
  } = useEnsureClone({
    projectId,
    issueId: open ? issueId : '', // Only load when dialog is open
  })

  // Agent prompts
  const { data: prompts, isLoading: promptsLoading } = useAgentPrompts(projectId)
  const startAgent = useStartAgent()

  // Selection state
  const [selectedModel, setSelectedModel] = useState<string>(() => {
    return localStorage.getItem(MODEL_STORAGE_KEY) ?? MODELS[0].value
  })
  const [selectedPromptId, setSelectedPromptId] = useState<string>(getInitialPromptId)

  // Starting state
  const [isStartingSession, setIsStartingSession] = useState(false)

  // Persist selections to localStorage
  useEffect(() => {
    localStorage.setItem(MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  useEffect(() => {
    if (selectedPromptId) {
      localStorage.setItem(PROMPT_STORAGE_KEY, selectedPromptId)
    }
  }, [selectedPromptId])

  // Compute effective prompt ID
  const effectivePromptId = useMemo(() => {
    if (!prompts || prompts.length === 0) {
      return ''
    }
    const selectedExists = prompts.some((p) => p.id === selectedPromptId)
    if (selectedExists) {
      return selectedPromptId
    }
    return prompts[0].id ?? ''
  }, [prompts, selectedPromptId])

  const selectedPrompt = prompts?.find((p) => p.id === effectivePromptId)

  // Handle start - creates clone then starts session
  const handleStart = useCallback(async () => {
    setIsStartingSession(true)
    try {
      // First, ensure clone exists
      const clonePath = await ensureClone()

      // Then start the session
      const session = await startAgent.mutateAsync({
        entityId: issueId,
        projectId,
        workingDirectory: clonePath,
        model: selectedModel,
        mode: selectedPrompt?.mode as SessionMode | undefined,
        systemPrompt: selectedPrompt?.initialMessage ?? undefined,
      })

      onSessionStart?.(session)
      onOpenChange(false) // Close dialog on success
    } catch (e) {
      onError?.(e as Error)
    } finally {
      setIsStartingSession(false)
    }
  }, [
    ensureClone,
    startAgent,
    issueId,
    projectId,
    selectedModel,
    selectedPrompt,
    onSessionStart,
    onOpenChange,
    onError,
  ])

  // Combined loading states
  const isLoading =
    isLoadingClone || promptsLoading || isCreating || isStartingSession || startAgent.isPending
  const isReady = !isLoadingClone && !promptsLoading && !isError && branchName && effectivePromptId

  // Don't render dialog content when closed
  if (!open) {
    return null
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Run Agent</DialogTitle>
          <DialogDescription>
            {branchName
              ? `Start an agent session on branch ${branchName}`
              : 'Configure and start an agent session'}
          </DialogDescription>
        </DialogHeader>

        <div className="py-4">
          {/* Initial loading state */}
          {(isLoadingClone || promptsLoading) && (
            <div className="flex items-center justify-center gap-2 py-8">
              <Loader variant="circular" size="sm" />
              <span className="text-muted-foreground text-sm">Preparing workspace...</span>
            </div>
          )}

          {/* Error state */}
          {isError && error && (
            <div className="border-destructive/50 bg-destructive/10 flex items-center gap-2 rounded-md border p-4">
              <AlertCircle className="text-destructive h-5 w-5 flex-shrink-0" />
              <div>
                <p className="text-destructive text-sm font-medium">Failed to prepare workspace</p>
                <p className="text-muted-foreground text-xs">{error.message}</p>
              </div>
            </div>
          )}

          {/* Launcher controls - show when ready */}
          {!isLoadingClone && !promptsLoading && !isError && branchName && (
            <div className="flex items-center gap-2">
              {/* Prompt selector */}
              <Select
                value={effectivePromptId}
                onValueChange={setSelectedPromptId}
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
                disabled={isLoading || !isReady}
                className="gap-1.5"
              >
                {isCreating || isStartingSession || startAgent.isPending ? (
                  <Loader variant="circular" size="sm" />
                ) : (
                  <Play className="h-3.5 w-3.5" />
                )}
                Start Agent
              </Button>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}
