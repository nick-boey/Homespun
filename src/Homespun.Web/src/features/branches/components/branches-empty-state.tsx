import { GitBranch } from 'lucide-react'

export interface BranchesEmptyStateProps {
  title?: string
  description?: string
}

export function BranchesEmptyState({
  title = 'No worktrees found',
  description = 'Create a worktree from a remote branch to get started.',
}: BranchesEmptyStateProps) {
  return (
    <div className="border-border flex flex-col items-center justify-center rounded-lg border border-dashed p-8 text-center">
      <GitBranch className="text-muted-foreground mb-4 h-12 w-12" />
      <h3 className="text-lg font-medium">{title}</h3>
      <p className="text-muted-foreground mt-1 text-sm">{description}</p>
    </div>
  )
}
