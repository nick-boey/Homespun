import { memo } from 'react'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'

export interface PrRowSkeletonProps {
  className?: string
}

/**
 * Skeleton loading state for a PR row.
 */
export const PrRowSkeleton = memo(function PrRowSkeleton({ className }: PrRowSkeletonProps) {
  return (
    <div
      data-testid="pr-row-skeleton"
      className={cn('flex items-center gap-3 rounded-lg border px-4 py-3', className)}
    >
      {/* PR Number skeleton */}
      <Skeleton className="h-5 w-10 shrink-0" />

      {/* Content skeleton */}
      <div className="min-w-0 flex-1 space-y-2">
        <Skeleton className="h-5 w-64" />
        <div className="flex gap-2">
          <Skeleton className="h-5 w-16" />
          <Skeleton className="h-5 w-24" />
          <Skeleton className="h-5 w-20" />
        </div>
      </div>

      {/* Link skeleton */}
      <Skeleton className="h-4 w-4 shrink-0" />
    </div>
  )
})
