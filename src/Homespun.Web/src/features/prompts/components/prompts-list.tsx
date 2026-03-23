import { useState, useMemo } from 'react'
import { Plus, RefreshCw, MessageSquare } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useProjectPrompts } from '../hooks/use-project-prompts'
import { useGlobalPrompts } from '../hooks/use-global-prompts'
import { useIssueAgentPrompts } from '../hooks/use-issue-agent-prompts'
import { useCreatePrompt } from '../hooks/use-create-prompt'
import { useUpdatePrompt } from '../hooks/use-update-prompt'
import { useDeletePrompt } from '../hooks/use-delete-prompt'
import { useApplyPromptChanges } from '../hooks/use-apply-prompt-changes'
import { useCreateOverride } from '../hooks/use-create-override'
import { useRemoveOverride } from '../hooks/use-remove-override'
import { PromptCard } from './prompt-card'
import { PromptForm } from './prompt-form'
import { PromptCardSkeleton } from './prompt-card-skeleton'
import { PromptsEmptyState } from './prompts-empty-state'
import { PromptsCodeEditor } from './prompts-code-editor'
import { IssueAgentPromptsSection } from './issue-agent-prompts-section'
import { SessionMode } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export interface PromptsListProps {
  projectId?: string
  isGlobal?: boolean
}

type ViewMode = 'list' | 'create' | 'edit'
type EditorMode = 'cards' | 'code'

export function PromptsList({ projectId, isGlobal = false }: PromptsListProps) {
  const [viewMode, setViewMode] = useState<ViewMode>('list')
  const [editorMode, setEditorMode] = useState<EditorMode>('cards')
  const [editingPrompt, setEditingPrompt] = useState<AgentPrompt | null>(null)
  const [deletingPromptId, setDeletingPromptId] = useState<string | null>(null)
  const [removingOverrideId, setRemovingOverrideId] = useState<string | null>(null)

  const globalPromptsQuery = useGlobalPrompts()
  const projectPromptsQuery = useProjectPrompts(projectId || '')
  const issueAgentPromptsQuery = useIssueAgentPrompts()

  const {
    data: prompts,
    isLoading,
    refetch,
    isRefetching,
  } = isGlobal ? globalPromptsQuery : projectPromptsQuery

  // Combine prompts for code editor view when on global page
  const allPromptsForCodeEditor = useMemo(() => {
    if (!isGlobal) {
      return prompts ?? []
    }
    // Combine global prompts + issue agent prompts for global page
    const regularPrompts = prompts ?? []
    const issuePrompts = issueAgentPromptsQuery.data ?? []
    return [...regularPrompts, ...issuePrompts]
  }, [isGlobal, prompts, issueAgentPromptsQuery.data])

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

  const applyPromptChanges = useApplyPromptChanges({
    projectId: isGlobal ? undefined : projectId,
    isGlobal,
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

  // Helper to determine if a prompt is a global prompt (not project-scoped and not already an override)
  const isGlobalPrompt = (prompt: AgentPrompt | null): boolean => {
    if (!prompt) return false
    // A global prompt has no projectId and is not marked as an override
    return !prompt.projectId && !prompt.isOverride
  }

  const handleEdit = (prompt: AgentPrompt) => {
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
    })
  }

  const handleUpdate = async (data: {
    name: string
    initialMessage?: string
    mode: SessionMode
  }) => {
    if (!editingPrompt?.id) return

    // If we're on the project prompts page and editing a global prompt,
    // create an override instead of updating the global prompt directly
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

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center justify-between">
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <MessageSquare className="h-5 w-5" />
            Agent Prompts
          </h2>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled>
              <RefreshCw className="mr-2 h-4 w-4" />
              Refresh
            </Button>
            <Button size="sm" disabled>
              <Plus className="mr-2 h-4 w-4" />
              New Prompt
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
      <div className="space-y-6">
        <div className="flex items-center gap-2">
          <MessageSquare className="h-5 w-5" />
          <h2 className="text-lg font-semibold">Create New Prompt</h2>
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
    const isCreatingOverride = !isGlobal && projectId && isGlobalPrompt(editingPrompt)
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-2">
          <MessageSquare className="h-5 w-5" />
          <h2 className="text-lg font-semibold">
            {isCreatingOverride ? 'Create Project Override' : 'Edit Prompt'}
          </h2>
        </div>
        <Card>
          <CardContent className="pt-6">
            <PromptForm
              prompt={editingPrompt}
              onSubmit={handleUpdate}
              onCancel={handleCancel}
              isSubmitting={updatePrompt.isPending || createOverride.isPending}
            />
          </CardContent>
        </Card>
      </div>
    )
  }

  const hasPrompts = prompts && prompts.length > 0

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="flex items-center gap-2 text-lg font-semibold">
          <MessageSquare className="h-5 w-5" />
          Agent Prompts
          {hasPrompts && (
            <span className="text-muted-foreground text-sm font-normal">({prompts.length})</span>
          )}
        </h2>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isRefetching}>
            <RefreshCw className={`mr-2 h-4 w-4 ${isRefetching ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
          {editorMode === 'cards' && (
            <Button size="sm" onClick={() => setViewMode('create')}>
              <Plus className="mr-2 h-4 w-4" />
              New Prompt
            </Button>
          )}
        </div>
      </div>

      <Tabs value={editorMode} onValueChange={(value) => setEditorMode(value as EditorMode)}>
        <TabsList>
          <TabsTrigger value="cards">Cards</TabsTrigger>
          <TabsTrigger value="code">Code</TabsTrigger>
        </TabsList>

        <TabsContent value="cards">
          {hasPrompts ? (
            <div className="grid gap-4">
              {prompts.map((prompt) => (
                <PromptCard
                  key={prompt.id}
                  prompt={prompt}
                  onEdit={handleEdit}
                  onDelete={handleDelete}
                  isDeleting={deletingPromptId === prompt.id}
                  onRemoveOverride={!isGlobal && projectId ? handleRemoveOverride : undefined}
                  isRemovingOverride={removingOverrideId === prompt.id}
                />
              ))}
            </div>
          ) : (
            <PromptsEmptyState />
          )}

          {/* Issue Agent Prompts section - only shown on global prompts page */}
          {isGlobal && (
            <div className="mt-6 border-t pt-6">
              <IssueAgentPromptsSection />
            </div>
          )}
        </TabsContent>

        <TabsContent value="code">
          <PromptsCodeEditor
            prompts={allPromptsForCodeEditor}
            onApply={applyPromptChanges.mutateAsync}
            isApplying={applyPromptChanges.isPending}
          />
        </TabsContent>
      </Tabs>
    </div>
  )
}
