import { useState } from 'react'
import { Pencil, Trash2, MoreHorizontal } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
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
import { SessionMode } from '@/api'
import type { AgentPrompt, SessionMode as SessionModeType } from '@/api/generated/types.gen'

export interface PromptCardProps {
  prompt: AgentPrompt
  onEdit: (prompt: AgentPrompt) => void
  /** Handler for delete action - required when showDelete is true */
  onDelete?: (promptId: string) => void
  isDeleting?: boolean
  /** Whether to show the delete option (defaults to true) */
  showDelete?: boolean
}

function getModeLabel(mode: SessionModeType | undefined): string {
  switch (mode) {
    case SessionMode.PLAN:
      return 'Plan'
    case SessionMode.BUILD:
      return 'Build'
    default:
      return 'Build'
  }
}

function getModeVariant(mode: SessionModeType | undefined): 'default' | 'secondary' {
  return mode === SessionMode.PLAN ? 'secondary' : 'default'
}

function formatDate(dateString: string | undefined): string {
  if (!dateString) return ''
  try {
    return new Date(dateString).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    })
  } catch {
    return ''
  }
}

function truncateText(text: string | undefined | null, maxLength: number): string {
  if (!text) return ''
  if (text.length <= maxLength) return text
  return text.slice(0, maxLength) + '...'
}

export function PromptCard({
  prompt,
  onEdit,
  onDelete,
  isDeleting,
  showDelete = true,
}: PromptCardProps) {
  const [showDeleteDialog, setShowDeleteDialog] = useState(false)

  const handleDelete = () => {
    if (prompt.id && onDelete) {
      onDelete(prompt.id)
    }
    setShowDeleteDialog(false)
  }

  return (
    <>
      <Card className="transition-shadow hover:shadow-md">
        <CardHeader className="pb-2">
          <div className="flex items-center justify-between">
            <CardTitle className="text-base font-medium">
              {prompt.name || 'Untitled Prompt'}
              {prompt.isOverride && (
                <span className="text-muted-foreground ml-1 font-normal">(project)</span>
              )}
            </CardTitle>
            <div className="flex items-center gap-2">
              <Badge variant={getModeVariant(prompt.mode)}>{getModeLabel(prompt.mode)}</Badge>
              {!prompt.projectId && <Badge variant="outline">Global</Badge>}
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" size="icon" className="h-8 w-8">
                    <MoreHorizontal className="h-4 w-4" />
                    <span className="sr-only">Actions</span>
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem onClick={() => onEdit(prompt)}>
                    <Pencil className="mr-2 h-4 w-4" />
                    Edit
                  </DropdownMenuItem>
                  {showDelete && (
                    <DropdownMenuItem
                      onClick={() => setShowDeleteDialog(true)}
                      className="text-destructive focus:text-destructive"
                    >
                      <Trash2 className="mr-2 h-4 w-4" />
                      Delete
                    </DropdownMenuItem>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <p className="text-muted-foreground line-clamp-2 text-sm">
            {truncateText(prompt.initialMessage, 150) || 'No system prompt content'}
          </p>
          <div className="mt-4 flex items-center justify-between">
            <span className="text-muted-foreground text-xs">
              {prompt.updatedAt ? `Updated ${formatDate(prompt.updatedAt)}` : ''}
            </span>
          </div>
        </CardContent>
      </Card>

      <AlertDialog open={showDeleteDialog} onOpenChange={setShowDeleteDialog}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Prompt</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete "{prompt.name}"? This action cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={handleDelete} disabled={isDeleting}>
              {isDeleting ? 'Deleting...' : 'Delete'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
