import { createFileRoute, useParams } from '@tanstack/react-router'
import { ClonesTab } from '@/features/clones'

export const Route = createFileRoute('/projects/$projectId/clones')({
  component: ClonesPage,
})

function ClonesPage() {
  const { projectId } = useParams({ from: '/projects/$projectId/clones' })
  return <ClonesTab projectId={projectId} />
}
