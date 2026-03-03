import { createFileRoute } from '@tanstack/react-router'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'

export const Route = createFileRoute('/settings')({
  component: Settings,
})

function Settings() {
  useBreadcrumbSetter([{ title: 'Settings' }], [])

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Settings</h1>
      <div className="border-border rounded-lg border p-8 text-center">
        <p className="text-muted-foreground">Global settings will be implemented here.</p>
      </div>
    </div>
  )
}
