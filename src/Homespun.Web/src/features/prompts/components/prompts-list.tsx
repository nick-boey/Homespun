import { useState, useMemo } from 'react'
import { Plus, RefreshCw, MessageSquare, RotateCcw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { useMergedProjectPrompts } from '../hooks/use-merged-project-prompts'
import { useGlobalPrompts } from '../hooks/use-global-prompts'
import { useIssueAgentPrompts } from '../hooks/use-issue-agent-prompts'
import { useCreatePrompt } from '../hooks/use-create-prompt'
import { useUpdatePrompt } from '../hooks/use-update-prompt'
import { useDeletePrompt } from '../hooks/use-delete-prompt'
import { useApplyPromptChanges } from '../hooks/use-apply-prompt-changes'
import { useCreateOverride } from '../hooks/use-create-override'
import { useRemoveOverride } from '../hooks/use-remove-override'
import { useRestoreDefaultPrompts } from '../hooks/use-restore-default-prompts'
import { useDeleteAllProjectPrompts } from '../hooks/use-delete-all-project-prompts'
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
  const [deletingPromptName, setDeletingPromptName] = useState<string | null>(null)
  const [removingOverrideName, setRemovingOverrideName] = useState<string | null>(null)
  const [showRestoreDialog, setShowRestoreDialog] = useState(false)

  const globalPromptsQuery = useGlobalPrompts()
  const mergedProjectPromptsQuery = useMergedProjectPrompts(projectId || '')
  const issueAgentPromptsQuery = useIssueAgentPrompts()

  const {
    data: prompts,
    isLoading,
    refetch,
    isRefetching,
  } = isGlobal ? globalPromptsQuery : mergedProjectPromptsQuery

  // Split prompts into project-scoped and inherited global for visual grouping
  const { projectPrompts, inheritedGlobalPrompts } = useMemo(() => {
    if (isGlobal || !prompts) return { projectPrompts: prompts ?? [], inheritedGlobalPrompts: [] }
    return {
      projectPrompts: prompts.filter((p) => !!p.projectId || !!p.isOverride),
      inheritedGlobalPrompts: prompts.filter((p) => !p.projectId && !p.isOverride),
    }
  }, [isGlobal, prompts])

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
      setDeletingPromptName(null)
    },
    onError: () => {
      setDeletingPromptName(null)
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
      setRemovingOverrideName(null)
    },
    onError: () => {
      setRemovingOverrideName(null)
    },
  })

  const restoreDefaults = useRestoreDefaultPrompts({
    onSuccess: () => {
      setShowRestoreDialog(false)
    },
  })

  const deleteAllProjectPrompts = useDeleteAllProjectPrompts({
    projectId: projectId || '',
    onSuccess: () => {
      setShowRestoreDialog(false)
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

  const handleDelete = async (promptName: string) => {
    setDeletingPromptName(promptName)
    await deletePrompt.mutateAsync({
      name: promptName,
      projectId: isGlobal ? undefined : projectId,
    })
  }

  const handleRemoveOverride = async (promptName: string) => {
    setRemovingOverrideName(promptName)
    await removeOverride.mutateAsync(promptName)
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
    if (!editingPrompt?.name) return

    // If we're on the project prompts page and editing a global prompt,
    // create an override instead of updating the global prompt directly
    if (!isGlobal && projectId && isGlobalPrompt(editingPrompt)) {
      await createOverride.mutateAsync({
        globalPromptName: editingPrompt.name,
        projectId: projectId,
        initialMessage: data.initialMessage,
      })
    } else {
      await updatePrompt.mutateAsync({
        name: editingPrompt.name,
        projectId: isGlobal ? undefined : projectId,
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
              <RotateCcw className="mr-2 h-4 w-4" />
              {isGlobal ? 'Restore Defaults' : 'Clear Project Prompts'}
            </Button>
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
          <Button
            variant="outline"
            size="sm"
            onClick={() => setShowRestoreDialog(true)}
            disabled={restoreDefaults.isPending || deleteAllProjectPrompts.isPending}
          >
            <RotateCcw className="mr-2 h-4 w-4" />
            {isGlobal ? 'Restore Defaults' : 'Clear Project Prompts'}
          </Button>
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
            isGlobal ? (
              <div className="grid gap-4">
                {prompts.map((prompt) => (
                  <PromptCard
                    key={prompt.name}
                    prompt={prompt}
                    onEdit={handleEdit}
                    onDelete={handleDelete}
                    isDeleting={deletingPromptName === prompt.name}
                    showDelete
                  />
                ))}
              </div>
            ) : (
              <div className="space-y-6">
                {projectPrompts.length > 0 && (
                  <div>
                    <h3 className="text-muted-foreground mb-3 text-sm font-medium">
                      Project Prompts
                    </h3>
                    <div className="grid gap-4">
                      {projectPrompts.map((prompt) => (
                        <PromptCard
                          key={prompt.name}
                          prompt={prompt}
                          onEdit={handleEdit}
                          onDelete={handleDelete}
                          isDeleting={deletingPromptName === prompt.name}
                          showDelete
                          onRemoveOverride={projectId ? handleRemoveOverride : undefined}
                          isRemovingOverride={removingOverrideName === prompt.name}
                        />
                      ))}
                    </div>
                  </div>
                )}
                {inheritedGlobalPrompts.length > 0 && (
                  <div>
                    <h3 className="text-muted-foreground mb-3 text-sm font-medium">
                      Inherited Global Prompts
                    </h3>
                    <div className="grid gap-4">
                      {inheritedGlobalPrompts.map((prompt) => (
                        <PromptCard
                          key={prompt.name}
                          prompt={prompt}
                          onEdit={handleEdit}
                          onDelete={handleDelete}
                          isDeleting={deletingPromptName === prompt.name}
                          showDelete={false}
                          onRemoveOverride={projectId ? handleRemoveOverride : undefined}
                          isRemovingOverride={removingOverrideName === prompt.name}
                        />
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )
          ) : (
            <PromptsEmptyState />
          )}

          {/* Issue Agent Prompts section */}
          <div className="mt-6 border-t pt-6">
            <IssueAgentPromptsSection projectId={isGlobal ? undefined : projectId} />
          </div>
        </TabsContent>

        <TabsContent value="code">
          <PromptsCodeEditor
            prompts={allPromptsForCodeEditor}
            onApply={applyPromptChanges.mutateAsync}
            isApplying={applyPromptChanges.isPending}
            globalPromptNames={
              !isGlobal && projectId
                ? (prompts ?? []).filter((p) => !p.projectId && !p.isOverride).map((p) => p.name!)
                : undefined
            }
          />
        </TabsContent>
      </Tabs>

      <AlertDialog open={showRestoreDialog} onOpenChange={setShowRestoreDialog}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {isGlobal ? 'Restore Default Prompts' : 'Clear Project Prompts'}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {isGlobal
                ? 'This will delete all global prompts and restore defaults. Project prompts are not affected.'
                : 'This will delete all project prompts. The project will revert to inherited global prompts.'}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (isGlobal) {
                  restoreDefaults.mutate()
                } else if (projectId) {
                  deleteAllProjectPrompts.mutate()
                }
              }}
            >
              {isGlobal ? 'Restore Defaults' : 'Clear Prompts'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
