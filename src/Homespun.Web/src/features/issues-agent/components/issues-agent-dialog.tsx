import { useState, useEffect, useCallback } from 'react'
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
import { Loader } from '@/components/ui/loader'
import {
  useCreateIssuesAgentSession,
  type CreateIssuesAgentSessionResult,
} from '../hooks/use-create-issues-agent-session'

const MODELS = [
  { value: 'sonnet', label: 'Sonnet' },
  { value: 'opus', label: 'Opus' },
  { value: 'haiku', label: 'Haiku' },
] as const

const MODEL_STORAGE_KEY = 'issues-agent-model'

export interface IssuesAgentDialogProps {
  /** Whether the dialog is open */
  open: boolean
  /** Callback when open state changes */
  onOpenChange: (open: boolean) => void
  /** The project ID */
  projectId: string
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
  onSessionCreated,
  onError,
}: IssuesAgentDialogProps) {
  const navigate = useNavigate()
  const createSession = useCreateIssuesAgentSession()

  // Model selection state
  const [selectedModel, setSelectedModel] = useState<string>(() => {
    return localStorage.getItem(MODEL_STORAGE_KEY) ?? MODELS[0].value // Default to Sonnet
  })

  // Persist model selection
  useEffect(() => {
    localStorage.setItem(MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  const handleStart = useCallback(async () => {
    try {
      const result = await createSession.mutateAsync({
        projectId,
        model: selectedModel,
      })

      onSessionCreated?.(result)

      // Navigate to the session
      navigate({ to: '/sessions/$sessionId', params: { sessionId: result.sessionId } })
      onOpenChange(false)
    } catch (e) {
      onError?.(e as Error)
    }
  }, [createSession, projectId, selectedModel, onSessionCreated, navigate, onOpenChange, onError])

  // Don't render dialog content when closed
  if (!open) {
    return null
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
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
