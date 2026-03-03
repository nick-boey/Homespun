import { createFileRoute, useParams } from '@tanstack/react-router'
import { SecretsList } from '@/features/secrets'

export const Route = createFileRoute('/projects/$projectId/secrets')({
  component: Secrets,
})

function Secrets() {
  const { projectId } = useParams({ from: '/projects/$projectId/secrets' })

  return <SecretsList projectId={projectId} />
}
