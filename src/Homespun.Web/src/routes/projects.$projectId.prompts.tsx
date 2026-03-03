import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/projects/$projectId/prompts')({
  component: Prompts,
})

function Prompts() {
  return (
    <div className="border-border rounded-lg border p-8 text-center">
      <p className="text-muted-foreground">Project prompts will be implemented here.</p>
    </div>
  )
}
