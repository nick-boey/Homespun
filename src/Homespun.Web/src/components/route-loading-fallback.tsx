import { memo } from 'react'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'

export interface RouteLoadingFallbackProps {
  className?: string
}

/**
 * Loading fallback for React Suspense at the route level.
 * Displays a generic page skeleton that matches the overall layout structure.
 */
export const RouteLoadingFallback = memo(function RouteLoadingFallback({
  className,
}: RouteLoadingFallbackProps) {
  return (
    <div
      data-testid="route-loading-fallback"
      className={cn('animate-in fade-in-0 space-y-6 duration-200', className)}
    >
      {/* Page header skeleton */}
      <div className="flex items-start justify-between">
        <div className="space-y-2">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-4 w-64" />
        </div>
        <Skeleton className="h-9 w-9" />
      </div>

      {/* Tab navigation skeleton */}
      <div className="border-border flex gap-1 border-b pb-0">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-10 w-20" />
        ))}
      </div>

      {/* Content area skeleton */}
      <div className="space-y-4">
        {/* Cards/List items skeleton */}
        {Array.from({ length: 3 }).map((_, i) => (
          <div key={i} className="border-border rounded-lg border p-4">
            <div className="flex items-start gap-4">
              <Skeleton className="h-10 w-10 rounded" />
              <div className="flex-1 space-y-2">
                <Skeleton className="h-5 w-40" />
                <Skeleton className="h-4 w-64" />
                <Skeleton className="h-4 w-32" />
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
})
