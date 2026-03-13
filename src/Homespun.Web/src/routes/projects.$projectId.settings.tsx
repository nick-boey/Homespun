import { createFileRoute, useParams } from '@tanstack/react-router'
import { RefreshCcw, Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { useFullRefresh } from '@/features/pull-requests/hooks'

export const Route = createFileRoute('/projects/$projectId/settings')({
  component: ProjectSettings,
})

function ProjectSettings() {
  const { projectId } = useParams({ from: '/projects/$projectId/settings' })
  const { fullRefresh, isPending } = useFullRefresh()

  const handleFullRefresh = async () => {
    try {
      const result = await fullRefresh(projectId)
      toast.success('Full refresh complete', {
        description: `Fetched ${result.openPrs} open PR(s) and ${result.closedPrs} closed PR(s)`,
      })
    } catch (error) {
      toast.error('Full refresh failed', {
        description: error instanceof Error ? error.message : 'An error occurred',
      })
    }
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>GitHub Synchronization</CardTitle>
          <CardDescription>Manage how pull requests are synchronized with GitHub.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex items-start justify-between gap-4">
            <div className="space-y-1">
              <h4 className="text-sm font-medium">Full Refresh</h4>
              <p className="text-muted-foreground text-sm">
                Download all open, closed, and merged pull requests from GitHub and update the
                cache. Use this to rebuild the PR cache from scratch.
              </p>
            </div>
            <Button variant="outline" onClick={handleFullRefresh} disabled={isPending}>
              {isPending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <RefreshCcw className="mr-2 h-4 w-4" />
              )}
              Full Refresh
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
