import { createFileRoute, useParams, Link } from '@tanstack/react-router'
import { Skeleton } from '@/components/ui/skeleton'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { RefreshCw } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Workflows } from '@/api'
import type { WorkflowExecution, StepExecution } from '@/api/generated/types.gen'

export const Route = createFileRoute(
  '/projects/$projectId/workflows/$workflowId/executions/$executionId'
)({
  component: ExecutionDetailPage,
})

function getStatusVariant(status?: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (status) {
    case 'completed':
      return 'default'
    case 'running':
    case 'queued':
      return 'secondary'
    case 'failed':
    case 'timedOut':
    case 'cancelled':
      return 'destructive'
    default:
      return 'outline'
  }
}

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return '—'
  return new Date(dateStr).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

function ExecutionDetailPage() {
  const { projectId, workflowId, executionId } = useParams({
    from: '/projects/$projectId/workflows/$workflowId/executions/$executionId',
  })

  const {
    data: execution,
    isLoading,
    isError,
    refetch,
  } = useQuery({
    queryKey: ['execution', executionId],
    queryFn: async () => {
      const response = await Workflows.getApiExecutionsByExecutionId({
        path: { executionId },
      })
      if (response.error || !response.data) {
        throw new Error('Execution not found')
      }
      return response.data as WorkflowExecution
    },
    enabled: !!executionId,
  })

  if (isLoading) {
    return (
      <div className="space-y-6" data-testid="execution-detail-loading">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-4 w-96" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }

  if (isError || !execution) {
    return (
      <div
        className="border-border rounded-lg border p-8 text-center"
        data-testid="execution-not-found"
      >
        <h2 className="text-xl font-semibold">Execution Not Found</h2>
        <p className="text-muted-foreground mt-2">
          The execution you&apos;re looking for doesn&apos;t exist.
        </p>
        <div className="mt-4 flex justify-center gap-2">
          <Button variant="outline" onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Try Again
          </Button>
          <Button asChild>
            <Link
              to="/projects/$projectId/workflows/$workflowId"
              params={{ projectId, workflowId }}
            >
              Back to Workflow
            </Link>
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <div className="flex items-center gap-3">
          <h2 className="text-xl font-semibold" data-testid="execution-title">
            Execution {execution.id?.slice(0, 8)}
          </h2>
          {execution.status && (
            <Badge variant={getStatusVariant(execution.status)}>{execution.status}</Badge>
          )}
        </div>
        <p className="text-muted-foreground mt-1">
          Workflow: {execution.workflowId?.slice(0, 8)} &middot; Version {execution.workflowVersion}
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="border-border rounded-lg border p-4">
          <p className="text-muted-foreground text-sm">Triggered By</p>
          <p className="mt-1 font-medium">{execution.triggeredBy ?? '—'}</p>
        </div>
        <div className="border-border rounded-lg border p-4">
          <p className="text-muted-foreground text-sm">Trigger Type</p>
          <p className="mt-1 font-medium">{execution.trigger?.type ?? '—'}</p>
        </div>
        <div className="border-border rounded-lg border p-4">
          <p className="text-muted-foreground text-sm">Started</p>
          <p className="mt-1 font-medium">{formatDate(execution.startedAt)}</p>
        </div>
        <div className="border-border rounded-lg border p-4">
          <p className="text-muted-foreground text-sm">Completed</p>
          <p className="mt-1 font-medium">{formatDate(execution.completedAt)}</p>
        </div>
      </div>

      {execution.errorMessage && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-4 dark:border-red-800 dark:bg-red-950">
          <p className="text-sm font-medium text-red-800 dark:text-red-200">Error</p>
          <p className="mt-1 text-sm text-red-700 dark:text-red-300">{execution.errorMessage}</p>
        </div>
      )}

      {execution.stepExecutions && execution.stepExecutions.length > 0 && (
        <div>
          <h3 className="mb-3 text-lg font-medium">Step Executions</h3>
          <div className="space-y-2">
            {execution.stepExecutions.map((step: StepExecution) => (
              <div
                key={step.stepId}
                className="border-border flex items-center justify-between rounded-lg border p-3"
              >
                <div>
                  <p className="font-medium">{step.stepId}</p>
                </div>
                {step.status && (
                  <Badge variant={getStatusVariant(step.status)}>{step.status}</Badge>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
