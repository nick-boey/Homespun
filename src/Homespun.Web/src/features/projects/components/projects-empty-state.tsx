import { Link } from '@tanstack/react-router'
import { FolderPlus } from 'lucide-react'
import { Button } from '@/components/ui/button'

export function ProjectsEmptyState() {
  return (
    <div className="flex flex-col items-center justify-center rounded-lg border border-dashed p-12 text-center">
      <FolderPlus className="text-muted-foreground/50 h-12 w-12" />
      <h3 className="mt-4 text-lg font-semibold">No projects yet</h3>
      <p className="text-muted-foreground mt-2 text-sm">
        Get started by creating your first project.
      </p>
      <Button asChild className="mt-6">
        <Link to="/projects/new">Create Project</Link>
      </Button>
    </div>
  )
}
