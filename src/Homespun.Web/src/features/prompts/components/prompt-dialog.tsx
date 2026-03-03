import { useState, useEffect } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogCancel,
} from '@/components/ui/alert-dialog'
import { Loader } from '@/components/ui/loader'
import { useCreatePrompt, useUpdatePrompt } from '../hooks'
import type { AgentPrompt, SessionMode } from '@/api/generated/types.gen'

interface PromptDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  projectId: string
  prompt?: AgentPrompt
}

export function PromptDialog({ open, onOpenChange, projectId, prompt }: PromptDialogProps) {
  const isEditing = !!prompt
  const createPrompt = useCreatePrompt()
  const updatePrompt = useUpdatePrompt()

  const [name, setName] = useState('')
  const [initialMessage, setInitialMessage] = useState('')
  const [mode, setMode] = useState<SessionMode>(1) // Default to Agent mode

  // Reset form when dialog opens/closes or prompt changes
  useEffect(() => {
    if (open && prompt) {
      setName(prompt.name ?? '')
      setInitialMessage(prompt.initialMessage ?? '')
      setMode(prompt.mode ?? 1)
    } else if (open && !prompt) {
      setName('')
      setInitialMessage('')
      setMode(1)
    }
  }, [open, prompt])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (!name.trim()) return

    if (isEditing && prompt?.id) {
      await updatePrompt.mutateAsync({
        id: prompt.id,
        request: {
          name: name.trim(),
          initialMessage: initialMessage.trim() || undefined,
          mode,
        },
        projectId,
      })
    } else {
      await createPrompt.mutateAsync({
        name: name.trim(),
        initialMessage: initialMessage.trim() || undefined,
        mode,
        projectId,
      })
    }

    onOpenChange(false)
  }

  const isLoading = createPrompt.isPending || updatePrompt.isPending

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent className="sm:max-w-lg">
        <form onSubmit={handleSubmit}>
          <AlertDialogHeader>
            <AlertDialogTitle>{isEditing ? 'Edit Prompt' : 'Create Prompt'}</AlertDialogTitle>
            <AlertDialogDescription>
              {isEditing
                ? 'Update the prompt configuration.'
                : 'Create a new prompt for agents to use when working on issues.'}
            </AlertDialogDescription>
          </AlertDialogHeader>

          <div className="grid gap-4 py-4">
            <div className="grid gap-2">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g., Build & Fix"
                required
              />
            </div>

            <div className="grid gap-2">
              <Label htmlFor="mode">Mode</Label>
              <Select
                value={String(mode)}
                onValueChange={(value) => setMode(Number(value) as SessionMode)}
              >
                <SelectTrigger id="mode">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="0">Plan Mode</SelectItem>
                  <SelectItem value="1">Agent Mode</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-muted-foreground text-xs">
                Plan mode requires approval before making changes. Agent mode executes directly.
              </p>
            </div>

            <div className="grid gap-2">
              <Label htmlFor="initialMessage">Initial Message (Optional)</Label>
              <Textarea
                id="initialMessage"
                value={initialMessage}
                onChange={(e) => setInitialMessage(e.target.value)}
                placeholder="Enter the initial message or instructions for the agent..."
                rows={6}
              />
              <p className="text-muted-foreground text-xs">
                Supports template variables: {'{{title}}'}, {'{{id}}'}, {'{{description}}'},{' '}
                {'{{branch}}'}, {'{{type}}'}
              </p>
            </div>
          </div>

          <AlertDialogFooter>
            <AlertDialogCancel type="button" disabled={isLoading}>
              Cancel
            </AlertDialogCancel>
            <Button type="submit" disabled={isLoading || !name.trim()}>
              {isLoading && <Loader variant="circular" size="sm" className="mr-2" />}
              {isEditing ? 'Save Changes' : 'Create Prompt'}
            </Button>
          </AlertDialogFooter>
        </form>
      </AlertDialogContent>
    </AlertDialog>
  )
}
