import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { Play, Trash2, Pencil, MoreHorizontal, Plus } from 'lucide-react'
import { useWorkflows, useDeleteWorkflow, useExecuteWorkflow } from '../hooks/use-workflows'
import { CreateWorkflowDialog } from './create-workflow-dialog'
import { ErrorFallback } from '@/components/error-boundary'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import type { WorkflowSummary } from '@/api/generated/types.gen'

interface WorkflowListProps {
  projectId: string
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
  })
}

export function WorkflowList({ projectId }: WorkflowListProps) {
  const { workflows, isLoading, isError, error, refetch, isFetching } = useWorkflows(projectId)
  const deleteWorkflow = useDeleteWorkflow(projectId)
  const executeWorkflow = useExecuteWorkflow()
  const [deleteTarget, setDeleteTarget] = useState<WorkflowSummary | null>(null)
  const [isRetrying, setIsRetrying] = useState(false)
  const [createDialogOpen, setCreateDialogOpen] = useState(false)

  const handleDelete = () => {
    if (deleteTarget?.id) {
      deleteWorkflow.mutate(deleteTarget.id)
      setDeleteTarget(null)
    }
  }

  const handleExecute = (workflowId: string) => {
    executeWorkflow.mutate(workflowId)
  }

  const handleRetry = async () => {
    setIsRetrying(true)
    try {
      await refetch()
    } finally {
      setIsRetrying(false)
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-3" data-testid="workflow-list-loading">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-16 w-full" />
      </div>
    )
  }

  if (isError) {
    return (
      <ErrorFallback
        error={error}
        title="Error loading workflows"
        description="Something went wrong while loading workflows."
        variant="inline"
        onRetry={handleRetry}
        isRetrying={isRetrying || isFetching}
      />
    )
  }

  if (workflows.length === 0) {
    return (
      <>
        <div
          className="border-border rounded-lg border p-8 text-center"
          data-testid="workflow-list-empty"
        >
          <h3 className="text-lg font-medium">No workflows yet</h3>
          <p className="text-muted-foreground mt-1">
            Create a workflow to automate tasks for this project.
          </p>
          <Button className="mt-4" onClick={() => setCreateDialogOpen(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Create Workflow
          </Button>
        </div>
        <CreateWorkflowDialog
          open={createDialogOpen}
          onOpenChange={setCreateDialogOpen}
          projectId={projectId}
        />
      </>
    )
  }

  return (
    <>
      <div className="mb-4 flex justify-end">
        <Button onClick={() => setCreateDialogOpen(true)}>
          <Plus className="mr-2 h-4 w-4" />
          Create Workflow
        </Button>
      </div>
      <Table data-testid="workflow-list-table">
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Description</TableHead>
            <TableHead>Steps</TableHead>
            <TableHead>Last Execution</TableHead>
            <TableHead>Updated</TableHead>
            <TableHead className="w-[70px]" />
          </TableRow>
        </TableHeader>
        <TableBody>
          {workflows.map((workflow) => (
            <TableRow key={workflow.id} data-testid={`workflow-row-${workflow.id}`}>
              <TableCell>
                <Link
                  to="/projects/$projectId/workflows/$workflowId"
                  params={{ projectId, workflowId: workflow.id! }}
                  className="font-medium hover:underline"
                >
                  {workflow.title}
                </Link>
                {!workflow.enabled && (
                  <Badge variant="outline" className="ml-2">
                    Disabled
                  </Badge>
                )}
              </TableCell>
              <TableCell className="text-muted-foreground max-w-[300px] truncate">
                {workflow.description || '—'}
              </TableCell>
              <TableCell>{workflow.stepCount ?? 0}</TableCell>
              <TableCell>
                {workflow.lastExecutionStatus ? (
                  <Badge variant={getStatusVariant(workflow.lastExecutionStatus)}>
                    {workflow.lastExecutionStatus}
                  </Badge>
                ) : (
                  <span className="text-muted-foreground">Never</span>
                )}
              </TableCell>
              <TableCell className="text-muted-foreground">
                {formatDate(workflow.updatedAt)}
              </TableCell>
              <TableCell>
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="ghost" size="icon" aria-label="Workflow actions">
                      <MoreHorizontal className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onClick={() => handleExecute(workflow.id!)}>
                      <Play className="mr-2 h-4 w-4" />
                      Run
                    </DropdownMenuItem>
                    <DropdownMenuItem asChild>
                      <Link
                        to="/projects/$projectId/workflows/$workflowId"
                        params={{ projectId, workflowId: workflow.id! }}
                      >
                        <Pencil className="mr-2 h-4 w-4" />
                        Edit
                      </Link>
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem
                      className="text-destructive focus:text-destructive"
                      onClick={() => setDeleteTarget(workflow)}
                    >
                      <Trash2 className="mr-2 h-4 w-4" />
                      Delete
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <AlertDialog open={!!deleteTarget} onOpenChange={() => setDeleteTarget(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete workflow</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete &quot;{deleteTarget?.title}&quot;? This action cannot
              be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleDelete}>Delete</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <CreateWorkflowDialog
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
        projectId={projectId}
      />
    </>
  )
}
