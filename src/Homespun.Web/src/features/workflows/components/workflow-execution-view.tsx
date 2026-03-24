import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Workflows } from '@/api'
import type {
  WorkflowExecution,
  WorkflowDefinition,
  StepExecution,
  WorkflowStep,
} from '@/api/generated/types.gen'
import { useWorkflowExecution } from '../hooks/use-workflow-execution'
import type { StepStatusInfo } from '../hooks/use-workflow-execution'
import { WorkflowMermaidChart } from './workflow-mermaid-chart'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import {
  CheckCircle2,
  Circle,
  Loader2,
  XCircle,
  Clock,
  ExternalLink,
  ShieldCheck,
  ShieldX,
  Ban,
} from 'lucide-react'

export interface WorkflowExecutionViewProps {
  executionId: string
  projectId: string
  workflowId: string
}

function formatDuration(ms: number | null | undefined): string {
  if (ms == null) return '—'
  const totalSeconds = Math.floor(ms / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  if (minutes > 0) return `${minutes}m ${seconds}s`
  return `${seconds}s`
}

function getStatusVariant(status?: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (status) {
    case 'completed':
      return 'default'
    case 'running':
    case 'queued':
    case 'waitingForInput':
      return 'secondary'
    case 'failed':
    case 'timedOut':
    case 'cancelled':
      return 'destructive'
    default:
      return 'outline'
  }
}

function StepStatusIcon({ status }: { status?: string }) {
  switch (status) {
    case 'completed':
      return <CheckCircle2 className="h-4 w-4 text-green-500" />
    case 'running':
      return <Loader2 className="h-4 w-4 animate-spin text-blue-500" />
    case 'failed':
      return <XCircle className="h-4 w-4 text-red-500" />
    case 'waitingForInput':
      return <Clock className="h-4 w-4 text-yellow-500" />
    case 'skipped':
      return <Circle className="h-4 w-4 text-gray-400" />
    default:
      return <Circle className="h-4 w-4 text-gray-400" />
  }
}

function mergeStepStatus(
  stepExecution: StepExecution | undefined,
  liveStatus: StepStatusInfo | undefined
): {
  status: string
  retryCount?: number
  durationMs?: number | null
  sessionId?: string | null
  error?: string
} {
  const base = {
    status: stepExecution?.status ?? 'pending',
    retryCount: stepExecution?.retryCount,
    durationMs: stepExecution?.durationMs,
    sessionId: stepExecution?.sessionId,
    error: stepExecution?.errorMessage ?? undefined,
  }

  if (liveStatus) {
    return {
      ...base,
      status: liveStatus.status,
      retryCount: liveStatus.retryCount ?? base.retryCount,
      error: liveStatus.error ?? base.error,
    }
  }

  return base
}

function isActiveExecution(status?: string, liveStatus?: string | null): boolean {
  const effectiveStatus = liveStatus ?? status
  return (
    effectiveStatus === 'running' || effectiveStatus === 'queued' || effectiveStatus === 'paused'
  )
}

export function WorkflowExecutionView({
  executionId,
  projectId,
  workflowId,
}: WorkflowExecutionViewProps) {
  const queryClient = useQueryClient()
  const { stepStatuses, workflowStatus, workflowError, pendingGate } =
    useWorkflowExecution(executionId)

  const { data: execution, isLoading: executionLoading } = useQuery({
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

  const { data: workflow } = useQuery({
    queryKey: ['workflow', workflowId],
    queryFn: async () => {
      const response = await Workflows.getApiWorkflowsByWorkflowId({
        path: { workflowId },
      })
      if (response.error || !response.data) {
        throw new Error('Workflow not found')
      }
      return response.data as WorkflowDefinition
    },
    enabled: !!workflowId,
  })

  const cancelMutation = useMutation({
    mutationFn: async () => {
      await Workflows.postApiExecutionsByExecutionIdCancel({
        path: { executionId },
        body: { projectId, reason: 'Cancelled by user' },
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['execution', executionId] })
    },
  })

  const signalMutation = useMutation({
    mutationFn: async ({ stepId, status }: { stepId: string; status: string }) => {
      await Workflows.postApiExecutionsByExecutionIdStepsByStepIdSignal({
        path: { executionId, stepId },
        body: { projectId, status },
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['execution', executionId] })
    },
  })

  const steps = (workflow?.steps ?? []).filter((s): s is WorkflowStep & { id: string } => !!s.id)
  const stepMap = new Map(steps.map((s) => [s.id, s]))
  const executionStepMap = new Map(
    (execution?.stepExecutions ?? [])
      .filter((se): se is StepExecution & { stepId: string } => !!se.stepId)
      .map((se) => [se.stepId, se])
  )

  // Build step executions for the mermaid chart from live data
  const chartStepExecutions: StepExecution[] = steps.map((step) => {
    const live = stepStatuses[step.id]
    const stored = executionStepMap.get(step.id)
    return {
      stepId: step.id,
      stepIndex: live?.stepIndex ?? stored?.stepIndex ?? 0,
      status: live?.status ?? stored?.status ?? 'pending',
    }
  })

  if (executionLoading) {
    return (
      <div className="space-y-6" data-testid="execution-view-loading">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-64 w-full" />
        <Skeleton className="h-48 w-full" />
      </div>
    )
  }

  const showCancel = isActiveExecution(execution?.status ?? undefined, workflowStatus)

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h2 className="text-xl font-semibold">Execution {executionId.slice(0, 8)}</h2>
          <Badge variant={getStatusVariant(workflowStatus ?? execution?.status)}>
            {workflowStatus ?? execution?.status ?? 'unknown'}
          </Badge>
        </div>
        <div className="flex gap-2">
          {showCancel && (
            <Button
              variant="destructive"
              size="sm"
              onClick={() => cancelMutation.mutate()}
              disabled={cancelMutation.isPending}
            >
              <Ban className="mr-1 h-4 w-4" />
              Cancel
            </Button>
          )}
        </div>
      </div>

      {/* Workflow Error */}
      {workflowError && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-4 dark:border-red-800 dark:bg-red-950">
          <p className="text-sm font-medium text-red-800 dark:text-red-200">Workflow Error</p>
          <p className="mt-1 text-sm text-red-700 dark:text-red-300">{workflowError}</p>
        </div>
      )}

      {/* Gate Approval Card */}
      {pendingGate && (
        <Card data-testid="gate-approval-card">
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <Clock className="h-5 w-5 text-yellow-500" />
              Gate: {pendingGate.gateName}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-muted-foreground mb-4 text-sm">
              This workflow is waiting for approval to continue.
            </p>
            <div className="flex gap-2">
              <Button
                size="sm"
                onClick={() =>
                  signalMutation.mutate({
                    stepId: pendingGate.stepId,
                    status: 'approved',
                  })
                }
                disabled={signalMutation.isPending}
              >
                <ShieldCheck className="mr-1 h-4 w-4" />
                Approve
              </Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={() =>
                  signalMutation.mutate({
                    stepId: pendingGate.stepId,
                    status: 'rejected',
                  })
                }
                disabled={signalMutation.isPending}
              >
                <ShieldX className="mr-1 h-4 w-4" />
                Reject
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Mermaid Chart */}
      {steps.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Workflow Progress</CardTitle>
          </CardHeader>
          <CardContent>
            <WorkflowMermaidChart steps={steps} stepExecutions={chartStepExecutions} />
          </CardContent>
        </Card>
      )}

      {/* Step Timeline */}
      {steps.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Step Timeline</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-2">
              {steps.map((step) => {
                const merged = mergeStepStatus(executionStepMap.get(step.id), stepStatuses[step.id])
                const stepDef = stepMap.get(step.id)

                return (
                  <div
                    key={step.id}
                    className="border-border flex items-center justify-between rounded-lg border p-3"
                    data-testid={`step-row-${step.id}`}
                  >
                    <div className="flex items-center gap-3">
                      <StepStatusIcon status={merged.status} />
                      <div>
                        <p className="font-medium">{stepDef?.name ?? step.id}</p>
                        <div className="text-muted-foreground flex items-center gap-2 text-xs">
                          <Badge variant="outline" className="text-xs">
                            {stepDef?.stepType ?? 'unknown'}
                          </Badge>
                          {merged.retryCount != null && merged.retryCount > 0 && (
                            <span>Retry {merged.retryCount}</span>
                          )}
                          {merged.durationMs != null && (
                            <span>{formatDuration(merged.durationMs)}</span>
                          )}
                        </div>
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      <Badge variant={getStatusVariant(merged.status)}>{merged.status}</Badge>
                      {merged.sessionId && (
                        <Link
                          to="/sessions/$sessionId"
                          params={{ sessionId: merged.sessionId }}
                          data-testid={`session-link-${step.id}`}
                        >
                          <Button variant="ghost" size="sm">
                            <ExternalLink className="mr-1 h-3 w-3" />
                            Session
                          </Button>
                        </Link>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Step Error Details */}
      {steps.map((step) => {
        const merged = mergeStepStatus(executionStepMap.get(step.id), stepStatuses[step.id])
        if (!merged.error) return null
        return (
          <div
            key={`error-${step.id}`}
            className="rounded-lg border border-red-200 bg-red-50 p-4 dark:border-red-800 dark:bg-red-950"
          >
            <p className="text-sm font-medium text-red-800 dark:text-red-200">
              {stepMap.get(step.id)?.name ?? step.id} — Error
            </p>
            <p className="mt-1 text-sm text-red-700 dark:text-red-300">{merged.error}</p>
          </div>
        )
      })}
    </div>
  )
}
