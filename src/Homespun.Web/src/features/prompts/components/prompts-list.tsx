import { useState } from 'react'
import { Plus, Pencil, Trash2, Copy, FileText, Globe } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
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
import {
  useProjectPrompts,
  useAvailableGlobalPrompts,
  useDeletePrompt,
  useCreatePrompt,
} from '../hooks'
import { PromptDialog } from './prompt-dialog'
import type { AgentPrompt, SessionMode } from '@/api/generated/types.gen'

interface PromptsListProps {
  projectId: string
}

export function PromptsList({ projectId }: PromptsListProps) {
  const { prompts, isLoading, isError } = useProjectPrompts(projectId)
  const { globalPrompts, isLoading: globalLoading } = useAvailableGlobalPrompts(projectId)
  const deletePrompt = useDeletePrompt()
  const createPrompt = useCreatePrompt()

  const [editingPrompt, setEditingPrompt] = useState<AgentPrompt | null>(null)
  const [isCreating, setIsCreating] = useState(false)
  const [deletingPrompt, setDeletingPrompt] = useState<AgentPrompt | null>(null)

  const handleDelete = async () => {
    if (!deletingPrompt?.id) return
    await deletePrompt.mutateAsync({ id: deletingPrompt.id, projectId })
    setDeletingPrompt(null)
  }

  const handleCopyToProject = async (globalPrompt: AgentPrompt) => {
    await createPrompt.mutateAsync({
      name: globalPrompt.name ?? '',
      initialMessage: globalPrompt.initialMessage ?? '',
      mode: globalPrompt.mode,
      projectId,
    })
  }

  if (isLoading) {
    return <PromptsListSkeleton />
  }

  if (isError) {
    return (
      <div className="border-border rounded-lg border p-8 text-center">
        <p className="text-muted-foreground">
          Unable to load prompts. Please try refreshing the page.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">Agent Prompts</h2>
          <p className="text-muted-foreground text-sm">
            Manage prompts that agents can use when working on issues in this project.
          </p>
        </div>
        <Button onClick={() => setIsCreating(true)} className="gap-1.5">
          <Plus className="h-4 w-4" />
          Add Prompt
        </Button>
      </div>

      {/* Project prompts */}
      {prompts.length === 0 ? (
        <Card>
          <CardContent className="py-8 text-center">
            <FileText className="text-muted-foreground mx-auto mb-3 h-10 w-10" />
            <p className="text-muted-foreground mb-4">
              No project-specific prompts yet. Create one or copy from global prompts below.
            </p>
            <Button variant="outline" onClick={() => setIsCreating(true)}>
              Create First Prompt
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4">
          {prompts.map((prompt) => (
            <PromptCard
              key={prompt.id}
              prompt={prompt}
              onEdit={() => setEditingPrompt(prompt)}
              onDelete={() => setDeletingPrompt(prompt)}
            />
          ))}
        </div>
      )}

      {/* Global prompts section */}
      {!globalLoading && globalPrompts.length > 0 && (
        <div className="space-y-4">
          <div className="flex items-center gap-2">
            <Globe className="text-muted-foreground h-4 w-4" />
            <h3 className="text-muted-foreground text-sm font-medium">
              Available Global Prompts
            </h3>
          </div>
          <div className="grid gap-3">
            {globalPrompts.map((prompt) => (
              <GlobalPromptCard
                key={prompt.id}
                prompt={prompt}
                onCopy={() => handleCopyToProject(prompt)}
                isCopying={createPrompt.isPending}
              />
            ))}
          </div>
        </div>
      )}

      {/* Create/Edit Dialog */}
      <PromptDialog
        open={isCreating || !!editingPrompt}
        onOpenChange={(open) => {
          if (!open) {
            setIsCreating(false)
            setEditingPrompt(null)
          }
        }}
        projectId={projectId}
        prompt={editingPrompt ?? undefined}
      />

      {/* Delete Confirmation */}
      <AlertDialog open={!!deletingPrompt} onOpenChange={() => setDeletingPrompt(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Prompt</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete "{deletingPrompt?.name}"? This action cannot be
              undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleDelete}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}

interface PromptCardProps {
  prompt: AgentPrompt
  onEdit: () => void
  onDelete: () => void
}

function PromptCard({ prompt, onEdit, onDelete }: PromptCardProps) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex items-start justify-between">
          <div className="space-y-1">
            <CardTitle className="text-base">{prompt.name}</CardTitle>
            <CardDescription className="flex items-center gap-2">
              <Badge variant="outline" className="text-xs">
                {getModeLabel(prompt.mode)}
              </Badge>
            </CardDescription>
          </div>
          <div className="flex gap-1">
            <Button variant="ghost" size="icon" onClick={onEdit} aria-label="Edit prompt">
              <Pencil className="h-4 w-4" />
            </Button>
            <Button variant="ghost" size="icon" onClick={onDelete} aria-label="Delete prompt">
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground line-clamp-2 text-sm">
          {prompt.initialMessage || 'No initial message configured.'}
        </p>
      </CardContent>
    </Card>
  )
}

interface GlobalPromptCardProps {
  prompt: AgentPrompt
  onCopy: () => void
  isCopying: boolean
}

function GlobalPromptCard({ prompt, onCopy, isCopying }: GlobalPromptCardProps) {
  return (
    <Card className="bg-muted/30">
      <CardHeader className="py-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Globe className="text-muted-foreground h-4 w-4" />
            <span className="font-medium">{prompt.name}</span>
            <Badge variant="secondary" className="text-xs">
              {getModeLabel(prompt.mode)}
            </Badge>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={onCopy}
            disabled={isCopying}
            className="gap-1.5"
          >
            <Copy className="h-3.5 w-3.5" />
            Copy to Project
          </Button>
        </div>
      </CardHeader>
    </Card>
  )
}

function PromptsListSkeleton() {
  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="space-y-2">
          <Skeleton className="h-6 w-32" />
          <Skeleton className="h-4 w-64" />
        </div>
        <Skeleton className="h-9 w-28" />
      </div>
      <div className="space-y-4">
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-32 w-full" />
      </div>
    </div>
  )
}

function getModeLabel(mode: SessionMode | undefined): string {
  // SessionMode: 0 = Plan, 1 = Agent
  switch (mode) {
    case 0:
      return 'Plan Mode'
    case 1:
      return 'Agent Mode'
    default:
      return 'Agent Mode'
  }
}
