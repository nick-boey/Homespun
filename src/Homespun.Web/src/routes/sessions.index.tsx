import { createFileRoute } from '@tanstack/react-router'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'

export const Route = createFileRoute('/sessions/')({
  component: SessionsList,
})

function SessionsList() {
  useBreadcrumbSetter([{ title: 'Sessions' }], [])

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Sessions</h1>
      <div className="border-border rounded-lg border p-8 text-center">
        <p className="text-muted-foreground">Sessions list will be implemented here.</p>
      </div>
    </div>
  )
}
