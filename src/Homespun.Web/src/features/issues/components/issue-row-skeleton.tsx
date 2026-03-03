import { memo } from 'react'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'
import { ROW_HEIGHT } from './task-graph-svg'

export interface IssueRowSkeletonProps {
  className?: string
}

/**
 * Skeleton loading state for an issue row in the task graph.
 * Mimics the layout of TaskGraphIssueRow for smooth content transitions.
 */
export const IssueRowSkeleton = memo(function IssueRowSkeleton({
  className,
}: IssueRowSkeletonProps) {
  return (
    <div
      data-testid="issue-row-skeleton"
      className={cn('flex items-center gap-2', className)}
      style={{ height: ROW_HEIGHT }}
    >
      {/* SVG graph placeholder */}
      <Skeleton className="h-6 w-12 shrink-0" />

      {/* Issue ID skeleton */}
      <Skeleton className="h-4 w-14 shrink-0" />

      {/* Type badge skeleton */}
      <Skeleton className="h-5 w-12 shrink-0 rounded" />

      {/* Title skeleton - flexible width */}
      <Skeleton className="h-4 flex-1" />

      {/* Status badge skeleton */}
      <Skeleton className="h-5 w-16 shrink-0 rounded" />
    </div>
  )
})
