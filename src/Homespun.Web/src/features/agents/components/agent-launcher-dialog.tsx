import { useState, useEffect, useMemo, useCallback } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { Play, ChevronDown, ChevronUp, AlertCircle, ExternalLink } from 'lucide-react'
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
import { Label } from '@/components/ui/label'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { useRunAgent, useAgentPrompts } from '../hooks'
import { isAgentConflictError } from '../hooks/use-run-agent'
import { useProject } from '@/features/projects'
import { BaseBranchSelector } from './base-branch-selector'
import type { RunAgentResult } from '../hooks/use-run-agent'

const MODELS = [
  { value: 'opus', label: 'Opus' },
  { value: 'sonnet', label: 'Sonnet' },
  { value: 'haiku', label: 'Haiku' },
] as const

const MODEL_STORAGE_KEY = 'agent-launcher-model'
const PROMPT_STORAGE_KEY = 'agent-launcher-prompt'
const BASE_BRANCH_STORAGE_KEY = 'agent-launcher-base-branch'
const NONE_PROMPT_ID = '__none__'

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
  /** Callback when an agent is started */
  onAgentStart?: (result: RunAgentResult) => void
  /** Callback when there's an error starting the agent */
  onError?: (error: Error) => void
}

/**
 * Dialog component for launching an agent on an issue.
 *
 * This component uses the server-side run agent endpoint which:
 * 1. Returns 202 Accepted immediately and closes the dialog
 * 2. Handles clone creation and session startup in the background
 * 3. Sends SignalR notifications when the agent is ready or fails
 */
export function AgentLauncherDialog({
  open,
  onOpenChange,
  projectId,
  issueId,
  onAgentStart,
  onError,
}: AgentLauncherDialogProps) {
  const navigate = useNavigate()

  // Project data for repo path and default branch
  const { project, isLoading: projectLoading } = useProject(projectId)

  // Selection state
  const [selectedModel, setSelectedModel] = useState<string>(() => {
    return localStorage.getItem(MODEL_STORAGE_KEY) ?? MODELS[0].value // Default to Opus
  })
  const [selectedPromptId, setSelectedPromptId] = useState<string>(getInitialPromptId)
  const [selectedBaseBranch, setSelectedBaseBranch] = useState<string>(() => {
    return localStorage.getItem(BASE_BRANCH_STORAGE_KEY) ?? ''
  })
  const [showAdvancedSettings, setShowAdvancedSettings] = useState(false)

  // Conflict state - when an agent is already running on this issue
  const [conflictSessionId, setConflictSessionId] = useState<string | null>(null)

  // Compute effective base branch - use selected value or fall back to project default
  const effectiveBaseBranch = useMemo(() => {
    return selectedBaseBranch || project?.defaultBranch || ''
  }, [selectedBaseBranch, project?.defaultBranch])

  // Agent prompts
  const { data: prompts, isLoading: promptsLoading, isError, error } = useAgentPrompts(projectId)
  const runAgent = useRunAgent()

  // Persist selections to localStorage
  useEffect(() => {
    localStorage.setItem(MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  useEffect(() => {
    if (selectedPromptId) {
      localStorage.setItem(PROMPT_STORAGE_KEY, selectedPromptId)
    }
  }, [selectedPromptId])

  useEffect(() => {
    if (selectedBaseBranch) {
      localStorage.setItem(BASE_BRANCH_STORAGE_KEY, selectedBaseBranch)
    }
  }, [selectedBaseBranch])

  // Compute effective prompt ID
  const effectivePromptId = useMemo(() => {
    // Handle None selection
    if (selectedPromptId === NONE_PROMPT_ID) {
      return NONE_PROMPT_ID
    }

    if (!prompts || prompts.length === 0) {
      return ''
    }
    const selectedExists = prompts.some((p) => p.id === selectedPromptId)
    if (selectedExists) {
      return selectedPromptId
    }

    // Default to first prompt
    return prompts[0].id ?? ''
  }, [prompts, selectedPromptId])

  // Handle start - call server-side run agent endpoint
  // Returns 202 Accepted immediately, dialog closes, and SignalR notifies when agent is ready
  const handleStart = useCallback(async () => {
    // Clear any previous conflict state
    setConflictSessionId(null)

    try {
      const result = await runAgent.mutateAsync({
        issueId,
        projectId,
        promptId: effectivePromptId === NONE_PROMPT_ID ? null : effectivePromptId,
        model: selectedModel,
        baseBranch: effectiveBaseBranch || undefined,
      })

      // Agent startup is now async - dialog closes immediately
      // SignalR will notify when agent is ready via AgentStarting/SessionStarted events
      onAgentStart?.(result)
      onOpenChange(false) // Close dialog immediately on 202 Accepted
    } catch (e) {
      // Handle conflict error - agent already running or starting
      if (isAgentConflictError(e)) {
        setConflictSessionId(e.sessionId)
        return // Don't propagate error, we'll show UI for this
      }
      onError?.(e as Error)
    }
  }, [
    runAgent,
    issueId,
    projectId,
    effectivePromptId,
    selectedModel,
    effectiveBaseBranch,
    onAgentStart,
    onOpenChange,
    onError,
  ])

  // Handle navigating to the existing session
  const handleOpenExistingSession = useCallback(() => {
    if (conflictSessionId) {
      navigate({ to: '/sessions/$sessionId', params: { sessionId: conflictSessionId } })
      onOpenChange(false)
    }
  }, [conflictSessionId, navigate, onOpenChange])

  // Combined loading states
  const isLoading = projectLoading || promptsLoading || runAgent.isPending
  const isReady = !projectLoading && !promptsLoading && !isError && effectivePromptId

  // Don't render dialog content when closed
  if (!open) {
    return null
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Run Agent</DialogTitle>
          <DialogDescription>Configure and start an agent session on this issue</DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-4">
          {/* Initial loading state */}
          {(projectLoading || promptsLoading) && (
            <div className="flex items-center justify-center gap-2 py-8">
              <Loader variant="circular" size="sm" />
              <span className="text-muted-foreground text-sm">Loading prompts...</span>
            </div>
          )}

          {/* Error state */}
          {isError && error && (
            <div className="border-destructive/50 bg-destructive/10 flex items-center gap-2 rounded-md border p-4">
              <AlertCircle className="text-destructive h-5 w-5 flex-shrink-0" />
              <div>
                <p className="text-destructive text-sm font-medium">Failed to load prompts</p>
                <p className="text-muted-foreground text-xs">{error.message}</p>
              </div>
            </div>
          )}

          {/* Conflict state - agent already running */}
          {conflictSessionId && (
            <div className="flex flex-col gap-3 rounded-md border border-amber-500/50 bg-amber-500/10 p-4">
              <div className="flex items-center gap-2">
                <AlertCircle className="h-5 w-5 flex-shrink-0 text-amber-600 dark:text-amber-400" />
                <div>
                  <p className="text-sm font-medium text-amber-800 dark:text-amber-200">
                    Agent already running
                  </p>
                  <p className="text-xs text-amber-700 dark:text-amber-300">
                    An agent session is already active on this issue.
                  </p>
                </div>
              </div>
              <Button
                size="sm"
                variant="outline"
                className="gap-1.5 self-start"
                onClick={handleOpenExistingSession}
              >
                <ExternalLink className="h-3.5 w-3.5" />
                Open Existing Session
              </Button>
            </div>
          )}

          {/* Launcher controls - show when ready */}
          {!projectLoading && !promptsLoading && !isError && (
            <>
              {/* Main controls row */}
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
                    {/* "None" option always available as first option */}
                    <SelectItem value={NONE_PROMPT_ID}>
                      None - Start without prompt (Plan mode)
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
                <Button
                  size="sm"
                  onClick={handleStart}
                  disabled={isLoading || !isReady}
                  className="gap-1.5"
                >
                  {runAgent.isPending ? (
                    <Loader variant="circular" size="sm" />
                  ) : (
                    <Play className="h-3.5 w-3.5" />
                  )}
                  Start Agent
                </Button>
              </div>

              {/* More settings collapsible */}
              <Collapsible open={showAdvancedSettings} onOpenChange={setShowAdvancedSettings}>
                <CollapsibleTrigger asChild>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-muted-foreground hover:text-foreground gap-1 p-0 text-xs"
                  >
                    {showAdvancedSettings ? (
                      <>
                        <ChevronUp className="h-3 w-3" />
                        Hide settings
                      </>
                    ) : (
                      <>
                        <ChevronDown className="h-3 w-3" />
                        More settings
                      </>
                    )}
                  </Button>
                </CollapsibleTrigger>
                <CollapsibleContent className="mt-3 space-y-3">
                  {/* Base Branch Selector */}
                  <div className="space-y-1.5">
                    <Label htmlFor="base-branch" className="text-sm">
                      Base Branch
                    </Label>
                    <BaseBranchSelector
                      repoPath={project?.localPath ?? undefined}
                      defaultBranch={project?.defaultBranch}
                      value={effectiveBaseBranch}
                      onChange={setSelectedBaseBranch}
                      disabled={isLoading}
                      aria-label="Select base branch"
                    />
                    <p className="text-muted-foreground text-xs">
                      The branch to create the working branch from
                    </p>
                  </div>
                </CollapsibleContent>
              </Collapsible>
            </>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}
