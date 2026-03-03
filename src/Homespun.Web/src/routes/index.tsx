import { createFileRoute, Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { Plus } from 'lucide-react'

export const Route = createFileRoute('/')({
  component: ProjectsList,
})

function ProjectsList() {
  useBreadcrumbSetter([{ title: 'Projects' }], [])

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Projects</h1>
        <Button asChild>
          <Link to="/projects/new">
            <Plus className="h-4 w-4" />
            New Project
          </Link>
        </Button>
      </div>
      <div className="border-border rounded-lg border p-8 text-center">
        <p className="text-muted-foreground">
          No projects yet. Create your first project to get started.
        </p>
      </div>
    </div>
  )
}
