import { useState } from 'react'
import {
  GitBranch,
  Trash2,
  RefreshCw,
  Play,
  AlertTriangle,
  Clock,
} from 'lucide-react'
import { Card, CardHeader, CardTitle, CardDescription, CardAction } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { cn } from '@/lib/utils'
import type { BranchInfo } from '@/api/generated/types.gen'

export interface BranchCardProps {
  branch: BranchInfo
  projectId: string
  onPull: () => void
  onDelete: () => void
  onStartAgent?: (promptId?: string) => void
  isPulling?: boolean
  isDeleting?: boolean
  isMerged?: boolean
}

function formatRelativeTime(dateString: string): string {
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSecs = Math.floor(diffMs / 1000)
  const diffMins = Math.floor(diffSecs / 60)
  const diffHours = Math.floor(diffMins / 60)
  const diffDays = Math.floor(diffHours / 24)

  if (diffDays > 0) {
    return `${diffDays} day${diffDays > 1 ? 's' : ''} ago`
  }
  if (diffHours > 0) {
    return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`
  }
  if (diffMins > 0) {
    return `${diffMins} minute${diffMins > 1 ? 's' : ''} ago`
  }
  return 'just now'
}

function getBranchStatus(branch: BranchInfo): { label: string; variant: 'default' | 'secondary' | 'destructive' | 'outline' } {
  if (branch.behindCount && branch.behindCount > 0) {
    return { label: `${branch.behindCount} behind`, variant: 'destructive' }
  }
  if (branch.aheadCount && branch.aheadCount > 0) {
    return { label: `${branch.aheadCount} ahead`, variant: 'secondary' }
  }
  return { label: 'Up to date', variant: 'outline' }
}

export function BranchCard({
  branch,
  onPull,
  onDelete,
  onStartAgent,
  isPulling = false,
  isDeleting = false,
  isMerged = false,
}: BranchCardProps) {
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false)
  const status = getBranchStatus(branch)

  const handleDelete = () => {
    onDelete()
    setIsDeleteDialogOpen(false)
  }

  return (
    <Card className="hover:bg-muted/50 transition-colors">
      <CardHeader>
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0 flex-1">
            <CardTitle className="flex items-center gap-2 text-base">
              <GitBranch className="text-muted-foreground h-4 w-4 flex-shrink-0" />
              <span className="truncate font-mono text-sm">{branch.shortName}</span>
              <Badge variant={status.variant} className="flex-shrink-0 text-xs">
                {status.label}
              </Badge>
              {isMerged && (
                <Badge variant="secondary" className="flex-shrink-0 text-xs">
                  Merged
                </Badge>
              )}
            </CardTitle>
            <CardDescription className="mt-2 space-y-1">
              {branch.lastCommitMessage && (
                <span className="block truncate text-xs">{branch.lastCommitMessage}</span>
              )}
              <span className="text-muted-foreground/70 flex items-center gap-3 text-xs">
                {branch.lastCommitDate && (
                  <span className="flex items-center gap-1">
                    <Clock className="h-3 w-3" />
                    {formatRelativeTime(branch.lastCommitDate)}
                  </span>
                )}
                {branch.commitSha && (
                  <span className="font-mono">{branch.commitSha.slice(0, 7)}</span>
                )}
              </span>
            </CardDescription>
          </div>
          <CardAction className="flex items-center gap-1">
            <Button
              variant="ghost"
              size="icon"
              onClick={onPull}
              disabled={isPulling}
              aria-label="Pull latest changes"
              title="Pull latest changes"
            >
              <RefreshCw className={cn('h-4 w-4', isPulling && 'animate-spin')} />
            </Button>
            {onStartAgent && (
              <Button
                variant="ghost"
                size="icon"
                onClick={() => onStartAgent()}
                aria-label="Start agent"
                title="Start agent"
              >
                <Play className="h-4 w-4" />
              </Button>
            )}
            <AlertDialog open={isDeleteDialogOpen} onOpenChange={setIsDeleteDialogOpen}>
              <AlertDialogTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  disabled={isDeleting}
                  aria-label="Delete worktree"
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Delete Worktree</AlertDialogTitle>
                  <AlertDialogDescription>
                    {isMerged ? (
                      <>
                        Are you sure you want to delete the worktree for "{branch.shortName}"?
                        This branch has been merged.
                      </>
                    ) : (
                      <>
                        <span className="text-destructive flex items-center gap-2 font-medium">
                          <AlertTriangle className="h-4 w-4" />
                          Warning: This branch has not been merged!
                        </span>
                        <span className="mt-2 block">
                          Deleting this worktree may result in lost work. Are you sure you want
                          to continue?
                        </span>
                      </>
                    )}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Cancel</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" onClick={handleDelete}>
                    Delete
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </CardAction>
        </div>
      </CardHeader>
    </Card>
  )
}
