import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/projects/$projectId/issues/')({
  component: IssuesList,
})

function IssuesList() {
  return (
    <div className="border-border rounded-lg border p-8 text-center">
      <p className="text-muted-foreground">Issues list will be implemented here.</p>
    </div>
  )
}
