import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Workflows, type WorkflowDefinition } from '@/api'
import { useTelemetry } from '@/hooks/use-telemetry'

export const workflowsQueryKey = (projectId: string) => ['workflows', projectId] as const

export const workflowQueryKey = (workflowId: string) => ['workflow', workflowId] as const

export const workflowExecutionsQueryKey = (workflowId: string) =>
  ['workflow-executions', workflowId] as const

export function useWorkflows(projectId: string) {
  const query = useQuery({
    queryKey: workflowsQueryKey(projectId),
    queryFn: async () => {
      const response = await Workflows.getApiProjectsByProjectIdWorkflows({
        path: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error('Failed to fetch workflows')
      }
      return response.data
    },
    enabled: !!projectId,
  })

  return {
    workflows: query.data?.workflows ?? [],
    totalCount: query.data?.totalCount ?? 0,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
    isFetching: query.isFetching,
  }
}

export function useWorkflow(workflowId: string) {
  const query = useQuery({
    queryKey: workflowQueryKey(workflowId),
    queryFn: async () => {
      const response = await Workflows.getApiWorkflowsByWorkflowId({
        path: { workflowId },
      })
      if (response.error || !response.data) {
        throw new Error('Workflow not found')
      }
      return response.data
    },
    enabled: !!workflowId,
  })

  return {
    workflow: query.data as WorkflowDefinition | undefined,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}

export function useWorkflowExecutions(workflowId: string, projectId?: string) {
  const query = useQuery({
    queryKey: workflowExecutionsQueryKey(workflowId),
    queryFn: async () => {
      const response = await Workflows.getApiWorkflowsByWorkflowIdExecutions({
        path: { workflowId },
        query: projectId ? { projectId } : undefined,
      })
      if (response.error || !response.data) {
        throw new Error('Failed to fetch executions')
      }
      return response.data
    },
    enabled: !!workflowId,
  })

  return {
    executions: query.data?.executions ?? [],
    totalCount: query.data?.totalCount ?? 0,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}

export function useDeleteWorkflow(projectId: string) {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async (workflowId: string) => {
      const response = await Workflows.deleteApiWorkflowsByWorkflowId({
        path: { workflowId },
      })
      if (response.error) {
        throw new Error('Failed to delete workflow')
      }
      return workflowId
    },
    onSuccess: (workflowId) => {
      telemetry.trackEvent('workflow_deleted', { workflowId })
      queryClient.invalidateQueries({ queryKey: workflowsQueryKey(projectId) })
    },
    onError: (error: Error, workflowId) => {
      telemetry.trackEvent('workflow_deletion_failed', {
        workflowId,
        error: error.message,
      })
    },
  })
}

export function useExecuteWorkflow() {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async (workflowId: string) => {
      const response = await Workflows.postApiWorkflowsByWorkflowIdExecute({
        path: { workflowId },
      })
      if (response.error || !response.data) {
        throw new Error('Failed to execute workflow')
      }
      return response.data
    },
    onSuccess: (data, workflowId) => {
      telemetry.trackEvent('workflow_executed', {
        workflowId,
        executionId: data.executionId ?? '',
      })
      queryClient.invalidateQueries({ queryKey: workflowExecutionsQueryKey(workflowId) })
      queryClient.invalidateQueries({ queryKey: workflowsQueryKey('') })
    },
    onError: (error: Error, workflowId) => {
      telemetry.trackEvent('workflow_execution_failed', {
        workflowId,
        error: error.message,
      })
    },
  })
}
