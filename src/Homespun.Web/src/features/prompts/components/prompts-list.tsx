import { useState } from 'react'
import { Plus, RefreshCw, MessageSquare } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { useProjectPrompts } from '../hooks/use-project-prompts'
import { useGlobalPrompts } from '../hooks/use-global-prompts'
import { useCreatePrompt } from '../hooks/use-create-prompt'
import { useUpdatePrompt } from '../hooks/use-update-prompt'
import { useDeletePrompt } from '../hooks/use-delete-prompt'
import { PromptCard } from './prompt-card'
import { PromptForm } from './prompt-form'
import { PromptCardSkeleton } from './prompt-card-skeleton'
import { PromptsEmptyState } from './prompts-empty-state'
import { SessionMode } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export interface PromptsListProps {
  projectId?: string
  isGlobal?: boolean
}

type ViewMode = 'list' | 'create' | 'edit'

export function PromptsList({ projectId, isGlobal = false }: PromptsListProps) {
  const [viewMode, setViewMode] = useState<ViewMode>('list')
  const [editingPrompt, setEditingPrompt] = useState<AgentPrompt | null>(null)
  const [deletingPromptId, setDeletingPromptId] = useState<string | null>(null)

  const globalPromptsQuery = useGlobalPrompts()
  const projectPromptsQuery = useProjectPrompts(projectId || '')

  const {
    data: prompts,
    isLoading,
    refetch,
    isRefetching,
  } = isGlobal ? globalPromptsQuery : projectPromptsQuery

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

  const handleEdit = (prompt: AgentPrompt) => {
    setEditingPrompt(prompt)
    setViewMode('edit')
  }

  const handleDelete = async (promptId: string) => {
    setDeletingPromptId(promptId)
    await deletePrompt.mutateAsync(promptId)
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
    await updatePrompt.mutateAsync({
      id: editingPrompt.id,
      name: data.name,
      initialMessage: data.initialMessage,
      mode: data.mode,
    })
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
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-2">
          <MessageSquare className="h-5 w-5" />
          <h2 className="text-lg font-semibold">Edit Prompt</h2>
        </div>
        <Card>
          <CardContent className="pt-6">
            <PromptForm
              prompt={editingPrompt}
              onSubmit={handleUpdate}
              onCancel={handleCancel}
              isSubmitting={updatePrompt.isPending}
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
          <Button size="sm" onClick={() => setViewMode('create')}>
            <Plus className="mr-2 h-4 w-4" />
            New Prompt
          </Button>
        </div>
      </div>

      {hasPrompts ? (
        <div className="grid gap-4">
          {prompts.map((prompt) => (
            <PromptCard
              key={prompt.id}
              prompt={prompt}
              onEdit={handleEdit}
              onDelete={handleDelete}
              isDeleting={deletingPromptId === prompt.id}
            />
          ))}
        </div>
      ) : (
        <PromptsEmptyState />
      )}
    </div>
  )
}
