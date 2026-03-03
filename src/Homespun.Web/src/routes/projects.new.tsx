import { createFileRoute, Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { ProjectCreationForm } from '@/features/projects'
import { ArrowLeft } from 'lucide-react'

export const Route = createFileRoute('/projects/new')({
  component: NewProject,
})

function NewProject() {
  useBreadcrumbSetter([{ title: 'Projects', url: '/' }, { title: 'New Project' }], [])

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link to="/">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h1 className="text-2xl font-semibold">New Project</h1>
      </div>
      <div className="max-w-lg">
        <ProjectCreationForm />
      </div>
    </div>
  )
}
