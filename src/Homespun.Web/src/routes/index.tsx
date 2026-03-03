import { createFileRoute, Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { Plus } from 'lucide-react'
import { ProjectsList } from '@/features/projects'

export const Route = createFileRoute('/')({
  component: ProjectsPage,
})

function ProjectsPage() {
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
      <ProjectsList />
    </div>
  )
}
