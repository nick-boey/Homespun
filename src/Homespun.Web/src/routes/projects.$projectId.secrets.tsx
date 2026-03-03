import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/projects/$projectId/secrets')({
  component: Secrets,
})

function Secrets() {
  return (
    <div className="border-border rounded-lg border p-8 text-center">
      <p className="text-muted-foreground">Project secrets will be implemented here.</p>
    </div>
  )
}
