import { createFileRoute } from '@tanstack/react-router'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { SessionsList } from '@/features/sessions'

export const Route = createFileRoute('/sessions/')({
  component: SessionsPage,
})

function SessionsPage() {
  useBreadcrumbSetter([{ title: 'Sessions' }], [])

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Sessions</h1>
      <SessionsList />
    </div>
  )
}
