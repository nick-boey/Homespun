import { createFileRoute, Navigate } from '@tanstack/react-router'

export const Route = createFileRoute('/projects/$projectId/')({
  component: ProjectIndex,
})

function ProjectIndex() {
  return <Navigate to="/projects/$projectId/issues" from="/projects/$projectId/" />
}
