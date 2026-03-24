import { useState, useMemo } from 'react'
import { ListTodo, Plus, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { useIssueAgentPrompts } from '../hooks/use-issue-agent-prompts'
import { useIssueAgentProjectPrompts } from '../hooks/use-issue-agent-project-prompts'
import { useCreatePrompt } from '../hooks/use-create-prompt'
import { useUpdatePrompt } from '../hooks/use-update-prompt'
import { useDeletePrompt } from '../hooks/use-delete-prompt'
import { useCreateOverride } from '../hooks/use-create-override'
import { useRemoveOverride } from '../hooks/use-remove-override'
import { PromptCard } from './prompt-card'
import { PromptForm } from './prompt-form'
import { PromptCardSkeleton } from './prompt-card-skeleton'
import { PromptsEmptyState } from './prompts-empty-state'
import { PromptCategory, SessionMode, SessionType } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export interface IssueAgentPromptsSectionProps {
  projectId?: string
}

type ViewMode = 'list' | 'create' | 'edit'

/**
 * Section displaying Issue Agent prompts with full CRUD support.
 * - User-selectable prompts (category=IssueAgent, no sessionType) get full CRUD
 * - System prompts (IssueAgentSystem) are shown in a separate read-only section
 * - On project pages, shows inherited global issue agent prompts with override support
 */
export function IssueAgentPromptsSection({ projectId }: IssueAgentPromptsSectionProps) {
  const [viewMode, setViewMode] = useState<ViewMode>('list')
  const [editingPrompt, setEditingPrompt] = useState<AgentPrompt | null>(null)
  const [deletingPromptId, setDeletingPromptId] = useState<string | null>(null)
  const [removingOverrideId, setRemovingOverrideId] = useState<string | null>(null)

  const isGlobal = !projectId
  const globalPromptsQuery = useIssueAgentPrompts()
  const projectPromptsQuery = useIssueAgentProjectPrompts(projectId || '')

  const {
    data: prompts,
    isLoading,
    refetch,
    isRefetching,
  } = isGlobal ? globalPromptsQuery : projectPromptsQuery

  // Split prompts into user-selectable, system, and for project pages: project vs inherited
  const { userPrompts, systemPrompts, projectUserPrompts, inheritedUserPrompts } = useMemo(() => {
    if (!prompts)
      return {
        userPrompts: [],
        systemPrompts: [],
        projectUserPrompts: [],
        inheritedUserPrompts: [],
      }

    const system = prompts.filter((p) => p.sessionType === SessionType.ISSUE_AGENT_SYSTEM)
    const user = prompts.filter((p) => p.sessionType !== SessionType.ISSUE_AGENT_SYSTEM)

    if (isGlobal) {
      return {
        userPrompts: user,
        systemPrompts: system,
        projectUserPrompts: [],
        inheritedUserPrompts: [],
      }
    }

    return {
      userPrompts: user,
      systemPrompts: system,
      projectUserPrompts: user.filter((p) => !!p.projectId || !!p.isOverride),
      inheritedUserPrompts: user.filter((p) => !p.projectId && !p.isOverride),
    }
  }, [prompts, isGlobal])

  const createPrompt = useCreatePrompt({
    projectId: isGlobal ? undefined : projectId,
    onSuccess: () => {
      setViewMode('list')
    },
  })

  const updatePrompt = useUpdatePrompt({
    projectId: isGlobal ? undefined : projectId,
    onSuccess: () => {
      setViewMode('list')
      setEditingPrompt(null)
    },
  })

  const deletePrompt = useDeletePrompt({
    projectId: isGlobal ? undefined : projectId,
    onSuccess: () => {
      setDeletingPromptId(null)
    },
    onError: () => {
      setDeletingPromptId(null)
    },
  })

  const createOverride = useCreateOverride({
    projectId: projectId || '',
    onSuccess: () => {
      setViewMode('list')
      setEditingPrompt(null)
    },
  })

  const removeOverride = useRemoveOverride({
    projectId: projectId || '',
    onSuccess: () => {
      setRemovingOverrideId(null)
    },
    onError: () => {
      setRemovingOverrideId(null)
    },
  })

  const isGlobalPrompt = (prompt: AgentPrompt | null): boolean => {
    if (!prompt) return false
    return !prompt.projectId && !prompt.isOverride
  }

  const handleEdit = (prompt: AgentPrompt) => {
    setEditingPrompt(prompt)
    setViewMode('edit')
  }

  const handleEditSystem = (prompt: AgentPrompt) => {
    setEditingPrompt(prompt)
    setViewMode('edit')
  }

  const handleDelete = async (promptId: string) => {
    setDeletingPromptId(promptId)
    await deletePrompt.mutateAsync(promptId)
  }

  const handleRemoveOverride = async (promptId: string) => {
    setRemovingOverrideId(promptId)
    await removeOverride.mutateAsync(promptId)
  }

  const handleCreate = async (data: {
    name: string
    initialMessage?: string
    mode: SessionMode
  }) => {
    await createPrompt.mutateAsync({
      name: data.name,
      initialMessage: data.initialMessage,
      mode: data.mode,
      projectId: isGlobal ? null : projectId,
      category: PromptCategory.ISSUE_AGENT,
    })
  }

  const handleUpdate = async (data: {
    name: string
    initialMessage?: string
    mode: SessionMode
  }) => {
    if (!editingPrompt?.id) return

    // If on project page and editing a global prompt, create override
    if (!isGlobal && projectId && isGlobalPrompt(editingPrompt)) {
      await createOverride.mutateAsync({
        globalPromptId: editingPrompt.id,
        projectId: projectId,
        initialMessage: data.initialMessage,
      })
    } else {
      await updatePrompt.mutateAsync({
        id: editingPrompt.id,
        name: data.name,
        initialMessage: data.initialMessage,
        mode: data.mode,
      })
    }
  }

  const handleCancel = () => {
    setViewMode('list')
    setEditingPrompt(null)
  }

  const isSystemPrompt = editingPrompt?.sessionType === SessionType.ISSUE_AGENT_SYSTEM
  const isCreatingOverride =
    !isGlobal && projectId && editingPrompt && isGlobalPrompt(editingPrompt)

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <ListTodo className="h-5 w-5" />
            Issue Agent Prompts
          </h2>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled>
              <RefreshCw className="mr-2 h-4 w-4" />
              Refresh
            </Button>
            <Button size="sm" disabled>
              <Plus className="mr-2 h-4 w-4" />
              New Issue Agent Prompt
            </Button>
          </div>
        </div>
        <div className="grid gap-4">
          <PromptCardSkeleton />
          <PromptCardSkeleton />
        </div>
      </div>
    )
  }

  if (viewMode === 'create') {
    return (
      <div className="space-y-4">
        <div className="flex items-center gap-2">
          <ListTodo className="h-5 w-5" />
          <h2 className="text-lg font-semibold">Create New Issue Agent Prompt</h2>
        </div>
        <Card>
          <CardContent className="pt-6">
            <PromptForm
              onSubmit={handleCreate}
              onCancel={handleCancel}
              isSubmitting={createPrompt.isPending}
            />
          </CardContent>
        </Card>
      </div>
    )
  }

  if (viewMode === 'edit' && editingPrompt) {
    return (
      <div className="space-y-4">
        <div className="flex items-center gap-2">
          <ListTodo className="h-5 w-5" />
          <h2 className="text-lg font-semibold">
            {isCreatingOverride
              ? 'Create Project Override'
              : isSystemPrompt
                ? 'Edit System Prompt'
                : 'Edit Issue Agent Prompt'}
          </h2>
        </div>
        <Card>
          <CardContent className="pt-6">
            <PromptForm
              prompt={editingPrompt}
              onSubmit={handleUpdate}
              onCancel={handleCancel}
              isSubmitting={updatePrompt.isPending || createOverride.isPending}
              hideNameField={isSystemPrompt}
            />
          </CardContent>
        </Card>
      </div>
    )
  }

  const hasUserPrompts = userPrompts.length > 0
  const hasSystemPrompts = systemPrompts.length > 0

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <ListTodo className="h-5 w-5" />
            Issue Agent Prompts
            {hasUserPrompts && (
              <span className="text-muted-foreground text-sm font-normal">
                ({userPrompts.length})
              </span>
            )}
          </h2>
          <p className="text-muted-foreground mt-1 text-sm">
            Prompts for the Issues Agent workflow. These are selectable when launching an issue
            agent.
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isRefetching}>
            <RefreshCw className={`mr-2 h-4 w-4 ${isRefetching ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
          <Button size="sm" onClick={() => setViewMode('create')}>
            <Plus className="mr-2 h-4 w-4" />
            New Issue Agent Prompt
          </Button>
        </div>
      </div>

      {hasUserPrompts ? (
        isGlobal ? (
          <div className="grid gap-4">
            {userPrompts.map((prompt) => (
              <PromptCard
                key={prompt.id}
                prompt={prompt}
                onEdit={handleEdit}
                onDelete={handleDelete}
                isDeleting={deletingPromptId === prompt.id}
                showDelete
              />
            ))}
          </div>
        ) : (
          <div className="space-y-6">
            {projectUserPrompts.length > 0 && (
              <div>
                <h3 className="text-muted-foreground mb-3 text-sm font-medium">
                  Project Issue Agent Prompts
                </h3>
                <div className="grid gap-4">
                  {projectUserPrompts.map((prompt) => (
                    <PromptCard
                      key={prompt.id}
                      prompt={prompt}
                      onEdit={handleEdit}
                      onDelete={handleDelete}
                      isDeleting={deletingPromptId === prompt.id}
                      showDelete
                      onRemoveOverride={projectId ? handleRemoveOverride : undefined}
                      isRemovingOverride={removingOverrideId === prompt.id}
                    />
                  ))}
                </div>
              </div>
            )}
            {inheritedUserPrompts.length > 0 && (
              <div>
                <h3 className="text-muted-foreground mb-3 text-sm font-medium">
                  Inherited Global Issue Agent Prompts
                </h3>
                <div className="grid gap-4">
                  {inheritedUserPrompts.map((prompt) => (
                    <PromptCard
                      key={prompt.id}
                      prompt={prompt}
                      onEdit={handleEdit}
                      showDelete={false}
                    />
                  ))}
                </div>
              </div>
            )}
          </div>
        )
      ) : (
        <PromptsEmptyState
          title="No issue agent prompts yet"
          description="Create a new issue agent prompt to get started."
        />
      )}

      {/* System Prompts section - read-only with edit-only support */}
      {hasSystemPrompts && (
        <div className="mt-6 border-t pt-6">
          <div>
            <h3 className="text-muted-foreground mb-1 text-sm font-medium">System Prompts</h3>
            <p className="text-muted-foreground mb-3 text-xs">
              System-level prompts that configure agent behavior. These can be edited but not
              deleted or created.
            </p>
          </div>
          <div className="grid gap-4">
            {systemPrompts.map((prompt) => (
              <PromptCard
                key={prompt.id}
                prompt={prompt}
                onEdit={handleEditSystem}
                showDelete={false}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
