import { Card, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

export function SessionCardSkeleton() {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0 flex-1 space-y-2">
            <div className="flex items-center gap-2">
              <Skeleton className="h-5 w-12" />
              <Skeleton className="h-4 w-24" />
            </div>
            <Skeleton className="h-5 w-48" />
          </div>
          <Skeleton className="h-6 w-16" />
        </div>
        <div className="mt-3 space-y-2">
          <div className="flex items-center gap-4">
            <Skeleton className="h-5 w-12" />
            <Skeleton className="h-4 w-16" />
            <Skeleton className="h-4 w-20" />
          </div>
          <div className="flex items-center gap-4">
            <Skeleton className="h-4 w-32" />
            <Skeleton className="h-4 w-28" />
          </div>
          <Skeleton className="h-4 w-24" />
        </div>
        <div className="mt-4 flex items-center gap-2">
          <Skeleton className="h-8 w-16" />
          <Skeleton className="h-8 w-16" />
        </div>
      </CardHeader>
    </Card>
  )
}
