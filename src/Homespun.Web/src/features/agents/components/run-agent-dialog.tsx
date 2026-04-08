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
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { Loader } from '@/components/ui/loader'
import { Label } from '@/components/ui/label'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { useRunAgent, useAgentPrompts } from '../hooks'
import { isAgentConflictError } from '../hooks/use-run-agent'
import { useProject } from '@/features/projects'
import { useWorkflows, useExecuteWorkflow } from '@/features/workflows'
import { BaseBranchSelector } from './base-branch-selector'
import { useIssueContext } from '@/features/sessions/hooks/use-issue-context'
import { renderPromptTemplate } from '@/features/sessions/utils/render-prompt-template'
import { useCreateIssuesAgentSession } from '@/features/issues-agent/hooks/use-create-issues-agent-session'
import { useIssueAgentAvailablePrompts } from '@/features/issues-agent/hooks/use-issue-agent-available-prompts'
import type { RunAgentResult } from '../hooks/use-run-agent'
import type { CreateIssuesAgentSessionResult } from '@/features/issues-agent/hooks/use-create-issues-agent-session'
import { SessionMode } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

const TASK_MODELS = [
  { value: 'opus', label: 'Opus' },
  { value: 'sonnet', label: 'Sonnet' },
  { value: 'haiku', label: 'Haiku' },
] as const

const ISSUES_MODELS = [
  { value: 'opus', label: 'Opus' },
  { value: 'sonnet', label: 'Sonnet' },
  { value: 'haiku', label: 'Haiku' },
] as const

// localStorage keys - kept separate for backward compatibility
const TASK_MODEL_STORAGE_KEY = 'agent-launcher-model'
const TASK_PROMPT_STORAGE_KEY = 'agent-launcher-prompt'
const TASK_BASE_BRANCH_STORAGE_KEY = 'agent-launcher-base-branch'
const TASK_MODE_STORAGE_KEY = 'agent-launcher-mode'
const ISSUES_MODEL_STORAGE_KEY = 'issues-agent-model'
const ISSUES_PROMPT_STORAGE_KEY = 'issues-agent-prompt'
const ISSUES_MODE_STORAGE_KEY = 'issues-agent-mode'
const NONE_PROMPT_ID = '__none__'

export interface RunAgentDialogProps {
  /** Whether the dialog is open */
  open: boolean
  /** Callback when open state changes */
  onOpenChange: (open: boolean) => void
  /** The project ID */
  projectId: string
  /** The issue ID - pre-selects task tab with this issue */
  issueId?: string
  /** Optional selected issue ID passed to issues agent tab */
  selectedIssueId?: string | null
  /** Default tab selection - defaults based on issueId presence */
  defaultTab?: 'task' | 'issues' | 'workflow'
  /** Callback when a task agent is started */
  onAgentStart?: (result: RunAgentResult) => void
  /** Callback when an issues agent session is created */
  onSessionCreated?: (result: CreateIssuesAgentSessionResult) => void
  /** Callback when there's an error */
  onError?: (error: Error) => void
}

/**
 * Unified dialog for launching both Task Agent and Issues Agent sessions.
 */
export function RunAgentDialog({
  open,
  onOpenChange,
  projectId,
  issueId,
  selectedIssueId,
  defaultTab,
  onAgentStart,
  onSessionCreated,
  onError,
}: RunAgentDialogProps) {
  // Determine initial tab - use a key to reset state when dialog context changes
  const computedDefaultTab = defaultTab ?? (issueId ? 'task' : 'issues')
  const [activeTab, setActiveTab] = useState<string>(computedDefaultTab)

  // Track the last computed default to detect changes
  const [lastDefault, setLastDefault] = useState(computedDefaultTab)
  if (computedDefaultTab !== lastDefault) {
    setLastDefault(computedDefaultTab)
    setActiveTab(computedDefaultTab)
  }

  // Wrap onOpenChange to clear issues tab instructions when dialog closes
  const handleOpenChange = useCallback(
    (newOpen: boolean) => {
      onOpenChange(newOpen)
    },
    [onOpenChange]
  )

  // Don't render dialog content when closed
  if (!open) {
    return null
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="flex h-[80vh] w-[80vw] max-w-none flex-col overflow-hidden sm:max-w-none">
        <DialogHeader>
          <DialogTitle>Run Agent</DialogTitle>
          <DialogDescription>Configure and start an agent session</DialogDescription>
        </DialogHeader>

        <Tabs
          value={activeTab}
          onValueChange={setActiveTab}
          className="flex flex-1 flex-col overflow-hidden"
        >
          <TabsList variant="line">
            <TabsTrigger value="task">Task Agent</TabsTrigger>
            <TabsTrigger value="issues">Issues Agent</TabsTrigger>
            <TabsTrigger value="workflow">Workflow</TabsTrigger>
          </TabsList>

          <TabsContent
            value="task"
            forceMount
            data-testid="task-tab-content"
            className="flex flex-1 flex-col overflow-hidden data-[state=inactive]:hidden"
          >
            <TaskAgentTabContent
              projectId={projectId}
              issueId={issueId ?? ''}
              onAgentStart={onAgentStart}
              onOpenChange={onOpenChange}
              onError={onError}
            />
          </TabsContent>

          <TabsContent
            value="issues"
            forceMount
            data-testid="issues-tab-content"
            className="flex flex-1 flex-col overflow-hidden data-[state=inactive]:hidden"
          >
            <IssuesAgentTabContent
              projectId={projectId}
              selectedIssueId={selectedIssueId}
              onSessionCreated={onSessionCreated}
              onOpenChange={onOpenChange}
              onError={onError}
            />
          </TabsContent>

          <TabsContent
            value="workflow"
            forceMount
            data-testid="workflow-tab-content"
            className="flex flex-1 flex-col overflow-hidden data-[state=inactive]:hidden"
          >
            <WorkflowTabContent
              projectId={projectId}
              issueId={issueId}
              onOpenChange={onOpenChange}
            />
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================================
// Task Agent Tab
// ============================================================================

interface TaskAgentTabContentProps {
  projectId: string
  issueId: string
  onAgentStart?: (result: RunAgentResult) => void
  onOpenChange: (open: boolean) => void
  onError?: (error: Error) => void
}

function TaskAgentTabContent({
  projectId,
  issueId,
  onAgentStart,
  onOpenChange,
  onError,
}: TaskAgentTabContentProps) {
  const navigate = useNavigate()

  // Project data for repo path and default branch
  const { project, isLoading: projectLoading } = useProject(projectId)

  // Issue context for rendering prompt placeholders
  const { data: issueContext } = useIssueContext(issueId, projectId)

  // Selection state
  const [selectedModel, setSelectedModel] = useState<string>(() => {
    return localStorage.getItem(TASK_MODEL_STORAGE_KEY) ?? TASK_MODELS[0].value
  })
  const [selectedPromptName, setSelectedPromptName] = useState<string>(() => {
    return localStorage.getItem(TASK_PROMPT_STORAGE_KEY) ?? ''
  })
  const [selectedBaseBranch, setSelectedBaseBranch] = useState<string>(() => {
    return localStorage.getItem(TASK_BASE_BRANCH_STORAGE_KEY) ?? ''
  })
  const [selectedMode, setSelectedMode] = useState<SessionMode>(() => {
    const stored = localStorage.getItem(TASK_MODE_STORAGE_KEY)
    return stored === SessionMode.BUILD ? SessionMode.BUILD : SessionMode.PLAN
  })
  const [showAdvancedSettings, setShowAdvancedSettings] = useState(false)
  const [userInstructions, setUserInstructions] = useState('')

  // Conflict state
  const [conflictSessionId, setConflictSessionId] = useState<string | null>(null)

  // Effective base branch
  const effectiveBaseBranch = useMemo(() => {
    return selectedBaseBranch || project?.defaultBranch || ''
  }, [selectedBaseBranch, project?.defaultBranch])

  // Agent prompts
  const { data: prompts, isLoading: promptsLoading, isError, error } = useAgentPrompts(projectId)
  const runAgent = useRunAgent()

  // Persist selections to localStorage
  useEffect(() => {
    localStorage.setItem(TASK_MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  useEffect(() => {
    if (selectedPromptName) {
      localStorage.setItem(TASK_PROMPT_STORAGE_KEY, selectedPromptName)
    }
  }, [selectedPromptName])

  useEffect(() => {
    if (selectedBaseBranch) {
      localStorage.setItem(TASK_BASE_BRANCH_STORAGE_KEY, selectedBaseBranch)
    }
  }, [selectedBaseBranch])

  useEffect(() => {
    localStorage.setItem(TASK_MODE_STORAGE_KEY, selectedMode)
  }, [selectedMode])

  // Compute effective prompt name
  const effectivePromptName = useMemo(() => {
    if (selectedPromptName === NONE_PROMPT_ID) {
      return NONE_PROMPT_ID
    }
    if (!prompts || prompts.length === 0) {
      return ''
    }
    const selectedExists = prompts.some((p) => p.name === selectedPromptName)
    if (selectedExists) {
      return selectedPromptName
    }
    return prompts[0].name ?? ''
  }, [prompts, selectedPromptName])

  // Sync mode from selected prompt
  useEffect(() => {
    if (effectivePromptName === NONE_PROMPT_ID || !effectivePromptName || !prompts) {
      setSelectedMode(SessionMode.PLAN)
      return
    }
    const prompt = prompts.find((p) => p.name === effectivePromptName)
    if (prompt?.mode) {
      setSelectedMode(prompt.mode)
    }
  }, [effectivePromptName, prompts])

  // Populate textarea when prompt or issue context changes
  useEffect(() => {
    if (
      effectivePromptName === NONE_PROMPT_ID ||
      !effectivePromptName ||
      !prompts ||
      !issueContext
    ) {
      setUserInstructions('')
      return
    }
    const prompt = prompts.find((p) => p.name === effectivePromptName)
    if (prompt?.initialMessage) {
      setUserInstructions(renderPromptTemplate(prompt.initialMessage, issueContext))
    } else {
      setUserInstructions('')
    }
  }, [effectivePromptName, prompts, issueContext])

  // Handle start
  const handleStart = useCallback(async () => {
    setConflictSessionId(null)

    try {
      const result = await runAgent.mutateAsync({
        issueId,
        projectId,
        mode: selectedMode,
        model: selectedModel,
        baseBranch: effectiveBaseBranch || undefined,
        userInstructions: userInstructions.trim() || undefined,
      })

      onAgentStart?.(result)
      onOpenChange(false)
    } catch (e) {
      if (isAgentConflictError(e)) {
        setConflictSessionId(e.sessionId)
        return
      }
      onError?.(e as Error)
    }
  }, [
    runAgent,
    issueId,
    projectId,
    selectedMode,
    selectedModel,
    effectiveBaseBranch,
    userInstructions,
    onAgentStart,
    onOpenChange,
    onError,
  ])

  // Handle navigating to existing session
  const handleOpenExistingSession = useCallback(() => {
    if (conflictSessionId) {
      navigate({ to: '/sessions/$sessionId', params: { sessionId: conflictSessionId } })
      onOpenChange(false)
    }
  }, [conflictSessionId, navigate, onOpenChange])

  // Combined loading states
  const isLoading = projectLoading || promptsLoading || runAgent.isPending
  const isReady = !projectLoading && !promptsLoading && !isError && effectivePromptName

  return (
    <div className="flex flex-1 flex-col gap-4 overflow-hidden py-4">
      {/* Loading state */}
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

      {/* Conflict state */}
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

      {/* Controls */}
      {!projectLoading && !promptsLoading && !isError && (
        <>
          {/* Main controls row */}
          <div className="flex items-center gap-2">
            {/* Prompt selector */}
            <PromptSelector
              prompts={prompts}
              effectivePromptName={effectivePromptName}
              onValueChange={setSelectedPromptName}
              disabled={isLoading || !prompts?.length}
              noneLabel="None - Start without prompt"
            />

            {/* Mode selector */}
            <ModeSelector
              value={selectedMode}
              onValueChange={setSelectedMode}
              disabled={isLoading}
            />

            {/* Model selector */}
            <Select value={selectedModel} onValueChange={setSelectedModel} disabled={isLoading}>
              <SelectTrigger className="w-24" aria-label="Select model">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TASK_MODELS.map((model) => (
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

          {/* Instructions textarea */}
          <Textarea
            placeholder="Additional instructions (optional)"
            value={userInstructions}
            onChange={(e) => setUserInstructions(e.target.value)}
            disabled={isLoading}
            className="flex-1 resize-none overflow-y-auto"
          />

          {/* Advanced settings */}
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
  )
}

// ============================================================================
// Issues Agent Tab
// ============================================================================

interface IssuesAgentTabContentProps {
  projectId: string
  selectedIssueId?: string | null
  onSessionCreated?: (result: CreateIssuesAgentSessionResult) => void
  onOpenChange: (open: boolean) => void
  onError?: (error: Error) => void
}

function IssuesAgentTabContent({
  projectId,
  selectedIssueId,
  onSessionCreated,
  onOpenChange,
  onError,
}: IssuesAgentTabContentProps) {
  const navigate = useNavigate()
  const createSession = useCreateIssuesAgentSession()

  // Issue context for rendering prompt placeholders
  const { data: issueContext } = useIssueContext(selectedIssueId, projectId)

  // Fetch available issue agent prompts
  const { data: prompts, isLoading: promptsLoading } = useIssueAgentAvailablePrompts(projectId)

  // Model selection state
  const [selectedModel, setSelectedModel] = useState<string>(() => {
    return localStorage.getItem(ISSUES_MODEL_STORAGE_KEY) ?? ISSUES_MODELS[0].value
  })

  // Prompt selection state
  const [selectedPromptName, setSelectedPromptName] = useState<string>(() => {
    return localStorage.getItem(ISSUES_PROMPT_STORAGE_KEY) ?? ''
  })

  // Mode selection state
  const [selectedMode, setSelectedMode] = useState<SessionMode>(() => {
    const stored = localStorage.getItem(ISSUES_MODE_STORAGE_KEY)
    return stored === SessionMode.PLAN ? SessionMode.PLAN : SessionMode.BUILD
  })

  // User instructions state
  const [userInstructions, setUserInstructions] = useState('')

  // Persist model selection
  useEffect(() => {
    localStorage.setItem(ISSUES_MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  // Persist prompt selection
  useEffect(() => {
    if (selectedPromptName) {
      localStorage.setItem(ISSUES_PROMPT_STORAGE_KEY, selectedPromptName)
    }
  }, [selectedPromptName])

  useEffect(() => {
    localStorage.setItem(ISSUES_MODE_STORAGE_KEY, selectedMode)
  }, [selectedMode])

  // Compute effective prompt name
  const effectivePromptName = useMemo(() => {
    if (selectedPromptName === NONE_PROMPT_ID) {
      return NONE_PROMPT_ID
    }
    if (!prompts || prompts.length === 0) {
      return NONE_PROMPT_ID
    }
    const selectedExists = prompts.some((p) => p.name === selectedPromptName)
    if (selectedExists) {
      return selectedPromptName
    }
    return prompts[0].name ?? ''
  }, [prompts, selectedPromptName])

  // Sync mode from selected prompt
  useEffect(() => {
    if (effectivePromptName === NONE_PROMPT_ID || !effectivePromptName || !prompts) {
      setSelectedMode(SessionMode.BUILD)
      return
    }
    const prompt = prompts.find((p) => p.name === effectivePromptName)
    if (prompt?.mode) {
      setSelectedMode(prompt.mode)
    }
  }, [effectivePromptName, prompts])

  // Populate textarea when prompt or issue context changes
  useEffect(() => {
    if (effectivePromptName === NONE_PROMPT_ID || !effectivePromptName || !prompts) {
      setUserInstructions('')
      return
    }
    const prompt = prompts.find((p) => p.name === effectivePromptName)
    if (prompt?.initialMessage && issueContext) {
      setUserInstructions(renderPromptTemplate(prompt.initialMessage, issueContext))
    } else if (prompt?.initialMessage) {
      // No issue context available yet - show template without placeholders filled
      setUserInstructions(prompt.initialMessage)
    } else {
      setUserInstructions('')
    }
  }, [effectivePromptName, prompts, issueContext])

  const hasPromptOrInstructions = useMemo(() => {
    return effectivePromptName !== NONE_PROMPT_ID || userInstructions.trim().length > 0
  }, [effectivePromptName, userInstructions])

  const handleStart = useCallback(async () => {
    try {
      const result = await createSession.mutateAsync({
        projectId,
        model: selectedModel,
        selectedIssueId: selectedIssueId ?? undefined,
        userInstructions: userInstructions.trim() || undefined,
        mode: selectedMode,
      })

      onSessionCreated?.(result)

      if (!hasPromptOrInstructions) {
        // Navigate to session when no prompt/instructions (interactive mode)
        navigate({ to: '/sessions/$sessionId', params: { sessionId: result.sessionId } })
      }

      onOpenChange(false)
    } catch (e) {
      onError?.(e as Error)
    }
  }, [
    createSession,
    projectId,
    selectedModel,
    selectedIssueId,
    userInstructions,
    selectedMode,
    hasPromptOrInstructions,
    onSessionCreated,
    navigate,
    onOpenChange,
    onError,
  ])

  return (
    <div className="flex flex-1 flex-col gap-4 overflow-hidden py-4">
      {/* Prompt selector */}
      <div className="space-y-2">
        {promptsLoading ? (
          <div className="flex items-center gap-2">
            <Loader variant="circular" size="sm" />
            <span className="text-muted-foreground text-sm">Loading prompts...</span>
          </div>
        ) : (
          <div className="flex items-center gap-2">
            <PromptSelector
              prompts={prompts}
              effectivePromptName={effectivePromptName}
              onValueChange={setSelectedPromptName}
              disabled={createSession.isPending}
              noneLabel="None - Start without prompt"
              showMode
            />

            {/* Mode selector */}
            <ModeSelector
              value={selectedMode}
              onValueChange={setSelectedMode}
              disabled={createSession.isPending}
            />

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
                {ISSUES_MODELS.map((model) => (
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
        )}
      </div>

      {/* Instructions textarea */}
      <Textarea
        placeholder="Additional instructions (optional)"
        value={userInstructions}
        onChange={(e) => setUserInstructions(e.target.value)}
        disabled={createSession.isPending}
        className="flex-1 resize-none overflow-y-auto"
      />

      <p className="text-muted-foreground text-xs">
        {userInstructions.trim()
          ? 'The agent will start with these instructions.'
          : 'Leave empty to start an interactive session.'}
      </p>
    </div>
  )
}

// ============================================================================
// Workflow Tab
// ============================================================================

const WORKFLOW_STORAGE_KEY = 'workflow-launcher-workflow'

interface WorkflowTabContentProps {
  projectId: string
  issueId?: string
  onOpenChange: (open: boolean) => void
}

function WorkflowTabContent({ projectId, issueId, onOpenChange }: WorkflowTabContentProps) {
  const { workflows, isLoading, isError, error } = useWorkflows(projectId)
  const executeWorkflow = useExecuteWorkflow()

  const enabledWorkflows = useMemo(() => workflows.filter((w) => w.enabled), [workflows])

  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string>(() => {
    return localStorage.getItem(WORKFLOW_STORAGE_KEY) ?? ''
  })

  // Compute effective workflow ID (ensure selection is valid)
  const effectiveWorkflowId = useMemo(() => {
    if (enabledWorkflows.length === 0) return ''
    const exists = enabledWorkflows.some((w) => w.id === selectedWorkflowId)
    if (exists) return selectedWorkflowId
    return enabledWorkflows[0].id ?? ''
  }, [enabledWorkflows, selectedWorkflowId])

  // Persist selection
  useEffect(() => {
    if (effectiveWorkflowId) {
      localStorage.setItem(WORKFLOW_STORAGE_KEY, effectiveWorkflowId)
    }
  }, [effectiveWorkflowId])

  const selectedWorkflow = useMemo(
    () => enabledWorkflows.find((w) => w.id === effectiveWorkflowId),
    [enabledWorkflows, effectiveWorkflowId]
  )

  const handleStart = useCallback(async () => {
    if (!effectiveWorkflowId) return

    try {
      const params: { workflowId: string; projectId: string; input?: Record<string, unknown> } = {
        workflowId: effectiveWorkflowId,
        projectId,
      }
      if (issueId) {
        params.input = { issueId }
      }
      await executeWorkflow.mutateAsync(params)
      onOpenChange(false)
    } catch {
      // Error is handled by mutation state
    }
  }, [effectiveWorkflowId, projectId, issueId, executeWorkflow, onOpenChange])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center gap-2 py-8">
        <Loader variant="circular" size="sm" />
        <span className="text-muted-foreground text-sm">Loading workflows...</span>
      </div>
    )
  }

  if (isError && error) {
    return (
      <div className="border-destructive/50 bg-destructive/10 flex items-center gap-2 rounded-md border p-4">
        <AlertCircle className="text-destructive h-5 w-5 flex-shrink-0" />
        <div>
          <p className="text-destructive text-sm font-medium">Failed to load workflows</p>
          <p className="text-muted-foreground text-xs">{error.message}</p>
        </div>
      </div>
    )
  }

  if (enabledWorkflows.length === 0) {
    return (
      <div className="py-8 text-center">
        <p className="text-muted-foreground text-sm">
          No enabled workflows available for this project.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-4 py-4">
      <div className="flex items-center gap-2">
        <Select
          value={effectiveWorkflowId}
          onValueChange={setSelectedWorkflowId}
          disabled={executeWorkflow.isPending}
        >
          <SelectTrigger className="flex-1" aria-label="Select workflow">
            <SelectValue placeholder="Select workflow" />
          </SelectTrigger>
          <SelectContent>
            {enabledWorkflows.map((wf) => (
              <SelectItem key={wf.id} value={wf.id!}>
                {wf.title}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Button
          size="sm"
          onClick={handleStart}
          disabled={executeWorkflow.isPending || !effectiveWorkflowId}
          className="gap-1.5"
        >
          {executeWorkflow.isPending ? (
            <Loader variant="circular" size="sm" />
          ) : (
            <Play className="h-3.5 w-3.5" />
          )}
          Start Workflow
        </Button>
      </div>

      {selectedWorkflow?.description && (
        <p className="text-muted-foreground text-xs">{selectedWorkflow.description}</p>
      )}
    </div>
  )
}

// ============================================================================
// Shared Components
// ============================================================================

interface PromptSelectorProps {
  prompts: AgentPrompt[] | undefined
  effectivePromptName: string
  onValueChange: (value: string) => void
  disabled: boolean
  noneLabel: string
  showMode?: boolean
}

function PromptSelector({
  prompts,
  effectivePromptName,
  onValueChange,
  disabled,
  noneLabel,
  showMode,
}: PromptSelectorProps) {
  return (
    <Select value={effectivePromptName} onValueChange={onValueChange} disabled={disabled}>
      <SelectTrigger className="w-40" aria-label="Select prompt">
        <SelectValue placeholder="Select prompt" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={NONE_PROMPT_ID}>{noneLabel}</SelectItem>
        {prompts?.map((prompt) => (
          <SelectItem key={prompt.name} value={prompt.name ?? ''}>
            {prompt.name}
            {showMode && prompt.mode ? ` (${prompt.mode})` : ''}
            {prompt.isOverride && ' (project)'}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

interface ModeSelectorProps {
  value: SessionMode
  onValueChange: (value: SessionMode) => void
  disabled: boolean
}

function ModeSelector({ value, onValueChange, disabled }: ModeSelectorProps) {
  return (
    <Select
      value={value}
      onValueChange={(v) => onValueChange(v as SessionMode)}
      disabled={disabled}
    >
      <SelectTrigger className="w-24" aria-label="Select mode">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={SessionMode.PLAN}>Plan</SelectItem>
        <SelectItem value={SessionMode.BUILD}>Build</SelectItem>
      </SelectContent>
    </Select>
  )
}
