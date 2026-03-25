import { ListChecks } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

export interface IssuesEmptyStateProps {
  onCreateIssue: () => void
  isCreating?: boolean
  className?: string
}

export function IssuesEmptyState({
  onCreateIssue,
  isCreating = false,
  className,
}: IssuesEmptyStateProps) {
  return (
    <div
      className={cn(
        'flex h-full flex-col items-center justify-center rounded-lg border border-dashed p-12 text-center',
        className
      )}
    >
      <ListChecks className="text-muted-foreground/50 h-12 w-12" />
      <h3 className="mt-4 text-lg font-semibold">No issues are currently open</h3>
      <p className="text-muted-foreground mt-2 text-sm">
        Get started by creating your first issue.
      </p>
      <Button className="mt-6" onClick={onCreateIssue} disabled={isCreating}>
        {isCreating ? 'Creating...' : 'Create an issue'}
      </Button>
    </div>
  )
}
