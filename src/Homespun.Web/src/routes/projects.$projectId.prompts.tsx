import { createFileRoute, useParams } from '@tanstack/react-router'
import { PromptsList } from '@/features/prompts'

export const Route = createFileRoute('/projects/$projectId/prompts')({
  component: Prompts,
})

function Prompts() {
  const { projectId } = useParams({ from: '/projects/$projectId/prompts' })

  return <PromptsList projectId={projectId} />
}
