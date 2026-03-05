import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

export function SessionCardSkeleton() {
  return (
    <Card data-testid="session-card-skeleton" className="rounded-lg border p-4 hover:bg-muted/50 transition-colors">
      <CardHeader className="p-0 space-y-2">
        {/* Title and stop button row */}
        <div className="flex items-start justify-between gap-2">
          <Skeleton data-testid="title-skeleton" className="h-6 w-3/4 animate-pulse" />
          <Skeleton className="h-8 w-8 animate-pulse" /> {/* Stop button skeleton */}
        </div>

        {/* Badges row */}
        <div data-testid="badges-skeleton" className="flex items-center gap-2">
          <Skeleton data-testid="type-badge-skeleton" className="h-5 w-12 animate-pulse" />
          <Skeleton data-testid="status-badge-skeleton" className="h-5 w-20 animate-pulse" />
          <Skeleton data-testid="pr-badge-skeleton" className="h-5 w-16 animate-pulse" />
        </div>
      </CardHeader>

      <CardContent className="p-0 space-y-4">
        {/* Description skeleton */}
        <div data-testid="description-skeleton" className="space-y-1 pt-3">
          <Skeleton data-testid="description-line-1-skeleton" className="h-4 w-full animate-pulse" />
          <Skeleton data-testid="description-line-2-skeleton" className="h-4 w-4/5 animate-pulse" />
        </div>

        {/* Divider */}
        <div className="border-t" />

        {/* Session info skeleton */}
        <div data-testid="session-info-skeleton" className="space-y-2">
          {/* Agent status row */}
          <div data-testid="agent-status-skeleton" className="flex items-center gap-2">
            <Skeleton className="h-3 w-3 rounded-full animate-pulse" />
            <Skeleton className="h-4 w-24 animate-pulse" />
            <Skeleton className="h-4 w-20 animate-pulse" />
          </div>

          {/* Mode, model, and time row */}
          <div className="flex items-center gap-2">
            <Skeleton data-testid="mode-skeleton" className="h-5 w-12 animate-pulse" />
            <Skeleton data-testid="model-skeleton" className="h-4 w-32 animate-pulse" />
            <Skeleton data-testid="time-skeleton" className="h-4 w-16 animate-pulse" />
          </div>
        </div>
      </CardContent>
    </Card>
  )
}