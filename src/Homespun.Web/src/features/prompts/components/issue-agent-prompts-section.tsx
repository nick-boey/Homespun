import { useState } from 'react'
import { ListTodo, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { useIssueAgentPrompts } from '../hooks/use-issue-agent-prompts'
import { useUpdatePrompt } from '../hooks/use-update-prompt'
import { PromptCard } from './prompt-card'
import { PromptForm } from './prompt-form'
import { PromptCardSkeleton } from './prompt-card-skeleton'
import { SessionMode } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

/**
 * Section displaying Issue Agent prompts (IssueAgentModification and IssueAgentSystem).
 * These are specialized prompts for the Issues Agent workflow.
 * Only editing is allowed - no create or delete.
 */
export function IssueAgentPromptsSection() {
  const [editingPrompt, setEditingPrompt] = useState<AgentPrompt | null>(null)

  const { data: prompts, isLoading, refetch, isRefetching } = useIssueAgentPrompts()

  const updatePrompt = useUpdatePrompt({
    projectId: undefined, // Global prompts
    onSuccess: () => {
      setEditingPrompt(null)
      refetch()
    },
  })

  const handleEdit = (prompt: AgentPrompt) => {
    setEditingPrompt(prompt)
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
    setEditingPrompt(null)
  }

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <ListTodo className="h-5 w-5" />
            Issue Agent Prompts
          </h2>
          <Button variant="outline" size="sm" disabled>
            <RefreshCw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
        <div className="grid gap-4">
          <PromptCardSkeleton />
          <PromptCardSkeleton />
        </div>
      </div>
    )
  }

  if (editingPrompt) {
    return (
      <div className="space-y-4">
        <div className="flex items-center gap-2">
          <ListTodo className="h-5 w-5" />
          <h2 className="text-lg font-semibold">Edit Issue Agent Prompt</h2>
        </div>
        <Card>
          <CardContent className="pt-6">
            <PromptForm
              prompt={editingPrompt}
              onSubmit={handleUpdate}
              onCancel={handleCancel}
              isSubmitting={updatePrompt.isPending}
              hideNameField
            />
          </CardContent>
        </Card>
      </div>
    )
  }

  const hasPrompts = prompts && prompts.length > 0

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <ListTodo className="h-5 w-5" />
            Issue Agent Prompts
            {hasPrompts && (
              <span className="text-muted-foreground text-sm font-normal">({prompts.length})</span>
            )}
          </h2>
          <p className="text-muted-foreground mt-1 text-sm">
            Fixed prompts for the Issues Agent workflow. These can be edited but not deleted.
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isRefetching}>
          <RefreshCw className={`mr-2 h-4 w-4 ${isRefetching ? 'animate-spin' : ''}`} />
          Refresh
        </Button>
      </div>

      {hasPrompts ? (
        <div className="grid gap-4">
          {prompts.map((prompt) => (
            <PromptCard key={prompt.id} prompt={prompt} onEdit={handleEdit} showDelete={false} />
          ))}
        </div>
      ) : (
        <Card>
          <CardContent className="text-muted-foreground py-8 text-center text-sm">
            No issue agent prompts found. Click refresh to reload.
          </CardContent>
        </Card>
      )}
    </div>
  )
}
