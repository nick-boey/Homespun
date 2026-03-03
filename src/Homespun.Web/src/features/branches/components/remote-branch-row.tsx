import { GitBranch, Download, Clock } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import type { BranchInfo } from '@/api/generated/types.gen'

export interface RemoteBranchRowProps {
  branch: BranchInfo
  onCreateWorktree: () => void
  isCreating?: boolean
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
    return `${diffDays}d ago`
  }
  if (diffHours > 0) {
    return `${diffHours}h ago`
  }
  if (diffMins > 0) {
    return `${diffMins}m ago`
  }
  return 'just now'
}

export function RemoteBranchRow({
  branch,
  onCreateWorktree,
  isCreating = false,
}: RemoteBranchRowProps) {
  return (
    <div className="hover:bg-muted/50 flex items-center justify-between gap-4 rounded-md border px-4 py-3 transition-colors">
      <div className="flex min-w-0 flex-1 items-center gap-3">
        <GitBranch className="text-muted-foreground h-4 w-4 flex-shrink-0" />
        <div className="min-w-0 flex-1">
          <span className="block truncate font-mono text-sm">{branch.shortName}</span>
          <span className="text-muted-foreground flex items-center gap-2 text-xs">
            {branch.lastCommitMessage && (
              <span className="max-w-[200px] truncate">{branch.lastCommitMessage}</span>
            )}
            {branch.lastCommitDate && (
              <span className="flex items-center gap-1">
                <Clock className="h-3 w-3" />
                {formatRelativeTime(branch.lastCommitDate)}
              </span>
            )}
          </span>
        </div>
      </div>
      <Button
        variant="outline"
        size="sm"
        onClick={onCreateWorktree}
        disabled={isCreating}
        className="flex-shrink-0"
      >
        <Download className={cn('mr-1 h-3 w-3', isCreating && 'animate-pulse')} />
        {isCreating ? 'Creating...' : 'Create Worktree'}
      </Button>
    </div>
  )
}
