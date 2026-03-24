import { Link } from '@tanstack/react-router'
import { RefreshCw } from 'lucide-react'
import { useWorkflow, useWorkflowExecutions, useUpdateWorkflow } from '../hooks/use-workflows'
import { WorkflowEditor } from './workflow-editor'
import { WorkflowTriggerCard } from './workflow-trigger-card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import type { ExecutionSummary } from '@/api/generated/types.gen'

interface WorkflowDetailProps {
  projectId: string
  workflowId: string
}

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

function formatDuration(ms?: number | null): string {
  if (!ms) return '—'
  const seconds = Math.floor(ms / 1000)
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  const remainingSeconds = seconds % 60
  return `${minutes}m ${remainingSeconds}s`
}

function ExecutionRow({
  execution,
  projectId,
}: {
  execution: ExecutionSummary
  projectId: string
}) {
  return (
    <TableRow data-testid={`execution-row-${execution.id}`}>
      <TableCell>
        <Link
          to="/projects/$projectId/workflows/$workflowId/executions/$executionId"
          params={{
            projectId,
            workflowId: execution.workflowId!,
            executionId: execution.id!,
          }}
          className="font-mono text-sm hover:underline"
        >
          {execution.id?.slice(0, 8)}
        </Link>
      </TableCell>
      <TableCell>
        {execution.status && (
          <Badge variant={getStatusVariant(execution.status)}>{execution.status}</Badge>
        )}
      </TableCell>
      <TableCell className="text-muted-foreground">{execution.triggerType ?? '—'}</TableCell>
      <TableCell className="text-muted-foreground">
        {formatDuration(execution.durationMs)}
      </TableCell>
      <TableCell className="text-muted-foreground">{formatDate(execution.createdAt)}</TableCell>
    </TableRow>
  )
}

export function WorkflowDetail({ projectId, workflowId }: WorkflowDetailProps) {
  const { workflow, isLoading, isError, refetch } = useWorkflow(workflowId)
  const updateWorkflow = useUpdateWorkflow(projectId)
  const {
    executions,
    isLoading: executionsLoading,
    refetch: refetchExecutions,
  } = useWorkflowExecutions(workflowId, projectId)

  if (isLoading) {
    return (
      <div className="space-y-6" data-testid="workflow-detail-loading">
        <div className="space-y-2">
          <Skeleton className="h-8 w-64" />
          <Skeleton className="h-4 w-96" />
        </div>
        <Skeleton className="h-10 w-48" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }

  if (isError || !workflow) {
    return (
      <div
        className="border-border rounded-lg border p-8 text-center"
        data-testid="workflow-not-found"
      >
        <h2 className="text-xl font-semibold">Workflow Not Found</h2>
        <p className="text-muted-foreground mt-2">
          The workflow you&apos;re looking for doesn&apos;t exist or has been deleted.
        </p>
        <div className="mt-4 flex justify-center gap-2">
          <Button variant="outline" onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Try Again
          </Button>
          <Button asChild>
            <Link to="/projects/$projectId/workflows" params={{ projectId }}>
              Back to Workflows
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
          <h2 className="text-xl font-semibold" data-testid="workflow-title">
            {workflow.title}
          </h2>
          <Badge variant={workflow.enabled ? 'default' : 'outline'}>
            {workflow.enabled ? 'Enabled' : 'Disabled'}
          </Badge>
        </div>
        {workflow.description && (
          <p className="text-muted-foreground mt-1" data-testid="workflow-description">
            {workflow.description}
          </p>
        )}
      </div>

      <Tabs defaultValue="editor">
        <TabsList>
          <TabsTrigger value="editor">Editor</TabsTrigger>
          <TabsTrigger value="trigger">Trigger</TabsTrigger>
          <TabsTrigger value="executions">Executions</TabsTrigger>
        </TabsList>

        <TabsContent value="editor" className="mt-4">
          <div data-testid="workflow-editor">
            <p className="text-muted-foreground mb-4 text-sm">
              {workflow.steps?.length ?? 0} steps &middot; Version {workflow.version}
            </p>
            <WorkflowEditor
              workflowId={workflowId}
              projectId={projectId}
              initialSteps={workflow.steps ?? []}
            />
          </div>
        </TabsContent>

        <TabsContent value="trigger" className="mt-4">
          <WorkflowTriggerCard
            trigger={workflow.trigger}
            onChange={(trigger) =>
              updateWorkflow.mutate({
                workflowId,
                request: { projectId, trigger },
              })
            }
          />
        </TabsContent>

        <TabsContent value="executions" className="mt-4">
          <div className="mb-4 flex items-center justify-between">
            <h3 className="text-lg font-medium">Execution History</h3>
            <Button variant="outline" size="sm" onClick={() => refetchExecutions()}>
              <RefreshCw className="mr-2 h-4 w-4" />
              Refresh
            </Button>
          </div>

          {executionsLoading ? (
            <div className="space-y-3" data-testid="executions-loading">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
            </div>
          ) : executions.length === 0 ? (
            <div
              className="border-border rounded-lg border p-8 text-center"
              data-testid="executions-empty"
            >
              <p className="text-muted-foreground">No executions yet.</p>
            </div>
          ) : (
            <Table data-testid="executions-table">
              <TableHeader>
                <TableRow>
                  <TableHead>ID</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Trigger</TableHead>
                  <TableHead>Duration</TableHead>
                  <TableHead>Started</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {executions.map((execution) => (
                  <ExecutionRow key={execution.id} execution={execution} projectId={projectId} />
                ))}
              </TableBody>
            </Table>
          )}
        </TabsContent>
      </Tabs>
    </div>
  )
}
