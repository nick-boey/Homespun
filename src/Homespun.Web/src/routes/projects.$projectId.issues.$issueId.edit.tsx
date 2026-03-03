import { createFileRoute, useParams, Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { ArrowLeft } from 'lucide-react'

export const Route = createFileRoute('/projects/$projectId/issues/$issueId/edit')({
  component: EditIssue,
})

function EditIssue() {
  const { issueId } = useParams({ from: '/projects/$projectId/issues/$issueId/edit' })

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link to="/projects/$projectId/issues" from="/projects/$projectId/issues/$issueId/edit">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h2 className="text-xl font-semibold">Edit Issue {issueId}</h2>
      </div>
      <div className="border-border rounded-lg border p-8 text-center">
        <p className="text-muted-foreground">Issue edit form will be implemented here.</p>
      </div>
    </div>
  )
}
