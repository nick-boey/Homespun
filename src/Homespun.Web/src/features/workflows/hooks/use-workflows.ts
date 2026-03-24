import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Workflows,
  WorkflowTemplate,
  type WorkflowDefinition,
  type UpdateWorkflowRequest,
} from '@/api'
import { useTelemetry } from '@/hooks/use-telemetry'

export const workflowsQueryKey = (projectId: string) => ['workflows', projectId] as const

export const workflowQueryKey = (workflowId: string, projectId: string) =>
  ['workflow', workflowId, projectId] as const

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

export function useWorkflow(workflowId: string, projectId: string) {
  const query = useQuery({
    queryKey: workflowQueryKey(workflowId, projectId),
    queryFn: async () => {
      const response = await Workflows.getApiWorkflowsByWorkflowId({
        path: { workflowId },
        query: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error('Workflow not found')
      }
      return response.data
    },
    enabled: !!workflowId && !!projectId,
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

export function useToggleWorkflowEnabled(projectId: string) {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async ({ workflowId, enabled }: { workflowId: string; enabled: boolean }) => {
      const response = await Workflows.putApiWorkflowsByWorkflowId({
        path: { workflowId },
        body: { projectId, enabled },
      })
      if (response.error) {
        throw new Error('Failed to toggle workflow')
      }
      return response.data as WorkflowDefinition
    },
    onSuccess: (_data, { workflowId }) => {
      telemetry.trackEvent('workflow_toggled', { workflowId })
      queryClient.invalidateQueries({ queryKey: workflowQueryKey(workflowId, projectId) })
      queryClient.invalidateQueries({ queryKey: workflowsQueryKey(projectId) })
    },
    onError: (error: Error, { workflowId }) => {
      telemetry.trackEvent('workflow_toggle_failed', {
        workflowId,
        error: error.message,
      })
    },
  })
}

export function useUpdateWorkflow(projectId: string) {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async ({
      workflowId,
      request,
    }: {
      workflowId: string
      request: UpdateWorkflowRequest
    }) => {
      const response = await Workflows.putApiWorkflowsByWorkflowId({
        path: { workflowId },
        body: request,
      })
      if (response.error) {
        throw new Error('Failed to update workflow')
      }
      return response.data as WorkflowDefinition
    },
    onSuccess: (_data, { workflowId }) => {
      telemetry.trackEvent('workflow_updated', { workflowId })
      queryClient.invalidateQueries({ queryKey: workflowQueryKey(workflowId, projectId) })
      queryClient.invalidateQueries({ queryKey: workflowsQueryKey(projectId) })
    },
    onError: (error: Error, { workflowId }) => {
      telemetry.trackEvent('workflow_update_failed', {
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

export function useCreateWorkflow(projectId: string) {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async ({ title, description }: { title: string; description?: string }) => {
      const response = await Workflows.postApiWorkflows({
        body: { projectId, title, description },
      })
      if (response.error || !response.data) {
        throw new Error('Failed to create workflow')
      }
      return response.data as WorkflowDefinition
    },
    onSuccess: (data) => {
      telemetry.trackEvent('workflow_created', { workflowId: data.id ?? '' })
      queryClient.invalidateQueries({ queryKey: workflowsQueryKey(projectId) })
    },
    onError: (error: Error) => {
      telemetry.trackEvent('workflow_creation_failed', { error: error.message })
    },
  })
}

export const workflowTemplatesQueryKey = ['workflow-templates'] as const

export function useWorkflowTemplates() {
  const query = useQuery({
    queryKey: workflowTemplatesQueryKey,
    queryFn: async () => {
      const response = await WorkflowTemplate.getApiWorkflowTemplates()
      if (response.error || !response.data) {
        throw new Error('Failed to fetch workflow templates')
      }
      return response.data
    },
  })

  return {
    templates: query.data ?? [],
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
  }
}

export function useCreateFromTemplate(projectId: string) {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async (templateId: string) => {
      const response = await WorkflowTemplate.postApiWorkflowTemplatesByTemplateIdCreate({
        path: { templateId },
        query: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error('Failed to create workflow from template')
      }
      return response.data as WorkflowDefinition
    },
    onSuccess: (data) => {
      telemetry.trackEvent('workflow_created_from_template', { workflowId: data.id ?? '' })
      queryClient.invalidateQueries({ queryKey: workflowsQueryKey(projectId) })
    },
    onError: (error: Error) => {
      telemetry.trackEvent('workflow_template_creation_failed', { error: error.message })
    },
  })
}
