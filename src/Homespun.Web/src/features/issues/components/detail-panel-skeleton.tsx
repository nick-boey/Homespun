import { memo } from 'react'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'

export interface DetailPanelSkeletonProps {
  className?: string
  /** Whether to show branch info skeleton */
  showBranch?: boolean
  /** Whether to show PR info skeleton */
  showPr?: boolean
  /** Whether to show agent status skeleton */
  showAgentStatus?: boolean
}

/**
 * Skeleton loading state for an expanded issue detail panel.
 * Mimics the layout of InlineIssueDetailRow for smooth content transitions.
 */
export const DetailPanelSkeleton = memo(function DetailPanelSkeleton({
  className,
  showBranch = true,
  showPr = false,
  showAgentStatus = false,
}: DetailPanelSkeletonProps) {
  return (
    <div
      data-testid="detail-panel-skeleton"
      className={cn('bg-muted/30 border-muted space-y-4 border-t px-3 py-4', className)}
    >
      {/* Header with badges */}
      <div className="flex flex-wrap items-center gap-2">
        {/* Issue ID skeleton */}
        <Skeleton className="h-5 w-20" />
        {/* Type badge skeleton */}
        <Skeleton className="h-5 w-12 rounded" />
        {/* Status badge skeleton */}
        <Skeleton className="h-5 w-16 rounded" />
        <div className="flex-1" />
        {/* Close button skeleton */}
        <Skeleton className="h-6 w-6 rounded" />
      </div>

      {/* Branch info skeleton */}
      {showBranch && (
        <div className="flex items-center gap-2">
          <Skeleton className="h-4 w-12" />
          <Skeleton className="h-6 w-48 rounded" />
          <Skeleton className="h-6 w-6 rounded" />
        </div>
      )}

      {/* PR info skeleton */}
      {showPr && (
        <div className="flex items-center gap-2">
          <Skeleton className="h-4 w-20" />
          <Skeleton className="h-4 w-12" />
          <Skeleton className="h-5 w-16 rounded" />
        </div>
      )}

      {/* Agent status skeleton */}
      {showAgentStatus && (
        <div className="flex items-center gap-2">
          <Skeleton className="h-4 w-12" />
          <Skeleton className="h-2 w-2 rounded-full" />
          <Skeleton className="h-4 w-16" />
        </div>
      )}

      {/* Description skeleton - multiple lines */}
      <div className="space-y-2">
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-4/5" />
        <Skeleton className="h-4 w-3/4" />
      </div>

      {/* Action buttons skeleton */}
      <div className="flex items-center gap-2">
        <Skeleton className="h-8 w-16 rounded" />
        <Skeleton className="h-8 w-24 rounded" />
      </div>
    </div>
  )
})
