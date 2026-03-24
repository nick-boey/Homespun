import { useState, useEffect, useMemo, useCallback } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { Play, ListTodo } from 'lucide-react'
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
import { Textarea } from '@/components/ui/textarea'
import { Label } from '@/components/ui/label'
import { Loader } from '@/components/ui/loader'
import { useIssue } from '@/features/issues/hooks/use-issue'
import {
  useCreateIssuesAgentSession,
  type CreateIssuesAgentSessionResult,
} from '../hooks/use-create-issues-agent-session'
import { useIssueAgentAvailablePrompts } from '../hooks/use-issue-agent-available-prompts'

const MODELS = [
  { value: 'sonnet', label: 'Sonnet' },
  { value: 'opus', label: 'Opus' },
  { value: 'haiku', label: 'Haiku' },
] as const

const MODEL_STORAGE_KEY = 'issues-agent-model'
const PROMPT_STORAGE_KEY = 'issues-agent-prompt'
const NONE_PROMPT_ID = '__none__'

/** Get the initial prompt ID from localStorage or return empty string */
function getInitialPromptId(): string {
  return localStorage.getItem(PROMPT_STORAGE_KEY) ?? ''
}

export interface IssuesAgentDialogProps {
  /** Whether the dialog is open */
  open: boolean
  /** Callback when open state changes */
  onOpenChange: (open: boolean) => void
  /** The project ID */
  projectId: string
  /** Optional selected issue ID to focus on */
  selectedIssueId?: string | null
  /** Callback when session is created */
  onSessionCreated?: (result: CreateIssuesAgentSessionResult) => void
  /** Callback when there's an error */
  onError?: (error: Error) => void
}

/**
 * Dialog for starting an Issues Agent session.
 * The Issues Agent is a specialized session for modifying Fleece issues.
 */
export function IssuesAgentDialog({
  open,
  onOpenChange,
  projectId,
  selectedIssueId,
  onSessionCreated,
  onError,
}: IssuesAgentDialogProps) {
  const navigate = useNavigate()
  const createSession = useCreateIssuesAgentSession()

  // Fetch selected issue if provided
  const { issue, isLoading: issueLoading } = useIssue(selectedIssueId ?? '', projectId)

  // Fetch available issue agent prompts
  const { data: prompts, isLoading: promptsLoading } = useIssueAgentAvailablePrompts(projectId)

  // Model selection state
  const [selectedModel, setSelectedModel] = useState<string>(() => {
    return localStorage.getItem(MODEL_STORAGE_KEY) ?? MODELS[0].value // Default to Sonnet
  })

  // Prompt selection state
  const [selectedPromptId, setSelectedPromptId] = useState<string>(getInitialPromptId)

  // User instructions state
  const [userInstructions, setUserInstructions] = useState('')

  // Wrap onOpenChange to clear instructions when dialog closes
  const handleOpenChange = useCallback(
    (newOpen: boolean) => {
      if (!newOpen) {
        setUserInstructions('')
      }
      onOpenChange(newOpen)
    },
    [onOpenChange]
  )

  // Persist model selection
  useEffect(() => {
    localStorage.setItem(MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  // Persist prompt selection
  useEffect(() => {
    if (selectedPromptId) {
      localStorage.setItem(PROMPT_STORAGE_KEY, selectedPromptId)
    }
  }, [selectedPromptId])

  // Compute effective prompt ID
  const effectivePromptId = useMemo(() => {
    // Handle None selection
    if (selectedPromptId === NONE_PROMPT_ID) {
      return NONE_PROMPT_ID
    }

    if (!prompts || prompts.length === 0) {
      return NONE_PROMPT_ID
    }

    const selectedExists = prompts.some((p) => p.id === selectedPromptId)
    if (selectedExists) {
      return selectedPromptId
    }

    // Default to first prompt
    return prompts[0].id ?? ''
  }, [prompts, selectedPromptId])

  const handleStart = useCallback(async () => {
    try {
      const result = await createSession.mutateAsync({
        projectId,
        model: selectedModel,
        selectedIssueId: selectedIssueId ?? undefined,
        userInstructions: userInstructions.trim() || undefined,
        promptId: effectivePromptId === NONE_PROMPT_ID ? null : effectivePromptId,
      })

      onSessionCreated?.(result)

      // Navigate to the session
      navigate({ to: '/sessions/$sessionId', params: { sessionId: result.sessionId } })
      handleOpenChange(false)
    } catch (e) {
      onError?.(e as Error)
    }
  }, [
    createSession,
    projectId,
    selectedModel,
    selectedIssueId,
    userInstructions,
    effectivePromptId,
    onSessionCreated,
    navigate,
    handleOpenChange,
    onError,
  ])

  // Don't render dialog content when closed
  if (!open) {
    return null
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <ListTodo className="h-5 w-5" />
            Start Issues Agent
          </DialogTitle>
          <DialogDescription>
            Create an AI agent session to analyze your codebase and modify issues. The agent can
            create, update, and reorganize issues using the Fleece CLI.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-4">
          {/* Selected issue display */}
          {selectedIssueId && (
            <div className="bg-muted/50 rounded-md border p-3">
              <div className="text-muted-foreground mb-1 text-xs font-medium">Selected Issue</div>
              {issueLoading ? (
                <div className="flex items-center gap-2">
                  <Loader variant="circular" size="sm" />
                  <span className="text-muted-foreground text-sm">Loading...</span>
                </div>
              ) : issue ? (
                <div className="flex items-start gap-2">
                  <code className="bg-muted rounded px-1.5 py-0.5 font-mono text-xs">
                    {issue.id}
                  </code>
                  <span className="text-sm">{issue.title}</span>
                </div>
              ) : (
                <div className="text-muted-foreground text-sm">Issue not found</div>
              )}
            </div>
          )}

          {/* Prompt selector */}
          <div className="space-y-2">
            <Label htmlFor="prompt-select" className="text-sm font-medium">
              Prompt
            </Label>
            {promptsLoading ? (
              <div className="flex items-center gap-2">
                <Loader variant="circular" size="sm" />
                <span className="text-muted-foreground text-sm">Loading prompts...</span>
              </div>
            ) : (
              <Select
                value={effectivePromptId}
                onValueChange={setSelectedPromptId}
                disabled={createSession.isPending}
              >
                <SelectTrigger id="prompt-select" aria-label="Select prompt">
                  <SelectValue placeholder="Select prompt" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE_PROMPT_ID}>
                    None - Start without prompt (Build mode)
                  </SelectItem>
                  {prompts?.map((prompt) => (
                    <SelectItem key={prompt.id} value={prompt.id ?? ''}>
                      {prompt.name}
                      {prompt.mode ? ` (${prompt.mode})` : ''}
                      {prompt.isOverride ? ' (project)' : ''}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </div>

          {/* Instructions textarea */}
          <div className="space-y-2">
            <Label htmlFor="instructions" className="text-sm font-medium">
              Instructions
            </Label>
            <Textarea
              id="instructions"
              placeholder="What would you like the agent to do? (optional)"
              value={userInstructions}
              onChange={(e) => setUserInstructions(e.target.value)}
              disabled={createSession.isPending}
              className="min-h-[80px] resize-none"
            />
            <p className="text-muted-foreground text-xs">
              {userInstructions.trim()
                ? 'The agent will start with these instructions.'
                : 'Leave empty to start an interactive session.'}
            </p>
          </div>

          {/* Main controls row */}
          <div className="flex items-center gap-2">
            {/* Model selector */}
            <Select
              value={selectedModel}
              onValueChange={setSelectedModel}
              disabled={createSession.isPending}
            >
              <SelectTrigger className="w-32" aria-label="Select model">
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
              disabled={createSession.isPending}
              className="gap-1.5"
            >
              {createSession.isPending ? (
                <Loader variant="circular" size="sm" />
              ) : (
                <Play className="h-3.5 w-3.5" />
              )}
              Start Agent
            </Button>
          </div>

          <p className="text-muted-foreground text-xs">
            The agent will work on a separate branch. After reviewing its changes, you can accept or
            reject them.
          </p>
        </div>
      </DialogContent>
    </Dialog>
  )
}
