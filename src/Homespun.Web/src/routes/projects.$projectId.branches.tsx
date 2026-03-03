import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/projects/$projectId/branches')({
  component: Branches,
})

function Branches() {
  return (
    <div className="border-border rounded-lg border p-8 text-center">
      <p className="text-muted-foreground">Branches list will be implemented here.</p>
    </div>
  )
}
