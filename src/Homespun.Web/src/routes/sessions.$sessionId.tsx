import { createFileRoute, useParams, Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { ArrowLeft } from 'lucide-react'

export const Route = createFileRoute('/sessions/$sessionId')({
  component: SessionChat,
})

function SessionChat() {
  const { sessionId } = useParams({ from: '/sessions/$sessionId' })

  useBreadcrumbSetter(
    [{ title: 'Sessions', url: '/sessions' }, { title: `Session ${sessionId.slice(0, 8)}...` }],
    [sessionId]
  )

  return (
    <div className="flex h-full flex-col space-y-4">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link to="/sessions">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h1 className="text-2xl font-semibold">Session {sessionId.slice(0, 8)}...</h1>
      </div>
      <div className="border-border flex-1 rounded-lg border p-8 text-center">
        <p className="text-muted-foreground">Session chat interface will be implemented here.</p>
      </div>
    </div>
  )
}
