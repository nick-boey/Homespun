import { Card, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

export function ProjectCardSkeleton() {
  return (
    <Card data-testid="project-card-skeleton">
      <CardHeader>
        <div className="space-y-3">
          <div className="flex items-center gap-2">
            <Skeleton className="h-5 w-5 rounded" />
            <Skeleton className="h-5 w-40" />
          </div>
          <Skeleton className="h-3 w-64" />
          <Skeleton className="h-3 w-32" />
        </div>
      </CardHeader>
    </Card>
  )
}
