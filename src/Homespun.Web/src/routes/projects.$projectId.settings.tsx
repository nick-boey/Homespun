import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/projects/$projectId/settings')({
  component: ProjectSettings,
})

function ProjectSettings() {
  return (
    <div className="border-border rounded-lg border p-8 text-center">
      <p className="text-muted-foreground">Project settings will be implemented here.</p>
    </div>
  )
}
