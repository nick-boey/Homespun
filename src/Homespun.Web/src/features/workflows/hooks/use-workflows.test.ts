import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import {
  useWorkflows,
  useWorkflow,
  useWorkflowExecutions,
  useDeleteWorkflow,
  useExecuteWorkflow,
  useCreateWorkflow,
  useToggleWorkflowEnabled,
  useWorkflowTemplates,
  useCreateFromTemplate,
} from './use-workflows'
import { Workflows, WorkflowTemplate } from '@/api'
import type {
  WorkflowSummary,
  WorkflowDefinition,
  ExecutionSummary,
  WorkflowTemplateSummary,
} from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Workflows: {
    getApiProjectsByProjectIdWorkflows: vi.fn(),
    getApiWorkflowsByWorkflowId: vi.fn(),
    getApiWorkflowsByWorkflowIdExecutions: vi.fn(),
    deleteApiWorkflowsByWorkflowId: vi.fn(),
    postApiWorkflowsByWorkflowIdExecute: vi.fn(),
    putApiWorkflowsByWorkflowId: vi.fn(),
    postApiWorkflows: vi.fn(),
  },
  WorkflowTemplate: {
    getApiWorkflowTemplates: vi.fn(),
    postApiWorkflowTemplatesByTemplateIdCreate: vi.fn(),
  },
}))

vi.mock('@/hooks/use-telemetry', () => ({
  useTelemetry: () => ({
    trackEvent: vi.fn(),
    trackPageView: vi.fn(),
    trackException: vi.fn(),
    trackDependency: vi.fn(),
  }),
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

const mockWorkflows: WorkflowSummary[] = [
  {
    id: 'wf-1',
    title: 'Build Pipeline',
    description: 'Runs CI build',
    enabled: true,
    triggerType: 'manual',
    stepCount: 3,
    version: 1,
    updatedAt: '2026-01-01T00:00:00Z',
    lastExecutionStatus: 'completed',
    lastExecutedAt: '2026-01-01T12:00:00Z',
  },
  {
    id: 'wf-2',
    title: 'Deploy Pipeline',
    description: 'Deploys to production',
    enabled: false,
    triggerType: 'event',
    stepCount: 5,
    version: 2,
    updatedAt: '2026-01-02T00:00:00Z',
    lastExecutionStatus: 'failed',
    lastExecutedAt: '2026-01-02T12:00:00Z',
  },
]

const mockWorkflowDefinition: WorkflowDefinition = {
  id: 'wf-1',
  projectId: 'proj-1',
  title: 'Build Pipeline',
  description: 'Runs CI build',
  steps: [],
  enabled: true,
  version: 1,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
}

const mockExecutions: ExecutionSummary[] = [
  {
    id: 'exec-1',
    workflowId: 'wf-1',
    workflowTitle: 'Build Pipeline',
    status: 'completed',
    triggerType: 'manual',
    createdAt: '2026-01-01T12:00:00Z',
    startedAt: '2026-01-01T12:00:01Z',
    completedAt: '2026-01-01T12:05:00Z',
    durationMs: 299000,
    triggeredBy: 'user',
  },
]

describe('useWorkflows', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches workflows successfully', async () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockResolvedValueOnce({
      data: { workflows: mockWorkflows, totalCount: 2 },
    })

    const { result } = renderHook(() => useWorkflows('proj-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.workflows).toEqual(mockWorkflows)
    expect(result.current.totalCount).toBe(2)
    expect(mock).toHaveBeenCalledWith({ path: { projectId: 'proj-1' } })
  })

  it('returns empty array when no workflows', async () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockResolvedValueOnce({
      data: { workflows: [], totalCount: 0 },
    })

    const { result } = renderHook(() => useWorkflows('proj-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.workflows).toEqual([])
    expect(result.current.totalCount).toBe(0)
  })

  it('handles fetch error', async () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockResolvedValueOnce({ error: { detail: 'Not found' } })

    const { result } = renderHook(() => useWorkflows('proj-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })

  it('does not fetch when projectId is empty', () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock

    renderHook(() => useWorkflows(''), {
      wrapper: createWrapper(),
    })

    expect(mock).not.toHaveBeenCalled()
  })
})

describe('useWorkflow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches a single workflow', async () => {
    const mock = Workflows.getApiWorkflowsByWorkflowId as Mock
    mock.mockResolvedValueOnce({ data: mockWorkflowDefinition })

    const { result } = renderHook(() => useWorkflow('wf-1', 'proj-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.workflow).toEqual(mockWorkflowDefinition)
    expect(mock).toHaveBeenCalledWith({
      path: { workflowId: 'wf-1' },
      query: { projectId: 'proj-1' },
    })
  })

  it('handles error when workflow not found', async () => {
    const mock = Workflows.getApiWorkflowsByWorkflowId as Mock
    mock.mockResolvedValueOnce({ error: { detail: 'Not found' } })

    const { result } = renderHook(() => useWorkflow('wf-missing', 'proj-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })
})

describe('useWorkflowExecutions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches executions for a workflow', async () => {
    const mock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock
    mock.mockResolvedValueOnce({
      data: { executions: mockExecutions, totalCount: 1 },
    })

    const { result } = renderHook(() => useWorkflowExecutions('wf-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.executions).toEqual(mockExecutions)
    expect(result.current.totalCount).toBe(1)
  })

  it('returns empty array when no executions', async () => {
    const mock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock
    mock.mockResolvedValueOnce({
      data: { executions: [], totalCount: 0 },
    })

    const { result } = renderHook(() => useWorkflowExecutions('wf-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.executions).toEqual([])
  })
})

describe('useDeleteWorkflow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deletes a workflow', async () => {
    const mock = Workflows.deleteApiWorkflowsByWorkflowId as Mock
    mock.mockResolvedValueOnce({})

    const { result } = renderHook(() => useDeleteWorkflow('proj-1'), {
      wrapper: createWrapper(),
    })

    result.current.mutate('wf-1')

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mock).toHaveBeenCalledWith({ path: { workflowId: 'wf-1' } })
  })
})

describe('useExecuteWorkflow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('executes a workflow', async () => {
    const mock = Workflows.postApiWorkflowsByWorkflowIdExecute as Mock
    mock.mockResolvedValueOnce({
      data: { executionId: 'exec-new', workflowId: 'wf-1', status: 'queued' },
    })

    const { result } = renderHook(() => useExecuteWorkflow(), {
      wrapper: createWrapper(),
    })

    result.current.mutate('wf-1')

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mock).toHaveBeenCalledWith({ path: { workflowId: 'wf-1' } })
  })
})

const mockCreatedWorkflow: WorkflowDefinition = {
  id: 'wf-new',
  projectId: 'proj-1',
  title: 'New Workflow',
  description: 'A new workflow',
  steps: [],
  enabled: true,
  version: 1,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
}

const mockTemplates: WorkflowTemplateSummary[] = [
  { id: 'tpl-1', title: 'CI Pipeline', description: 'Basic CI', stepCount: 3 },
  { id: 'tpl-2', title: 'Deploy', description: 'Deploy to prod', stepCount: 5 },
]

describe('useToggleWorkflowEnabled', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls update API with enabled: true to enable a workflow', async () => {
    const mock = Workflows.putApiWorkflowsByWorkflowId as Mock
    mock.mockResolvedValueOnce({ data: { ...mockWorkflowDefinition, enabled: true } })

    const { result } = renderHook(() => useToggleWorkflowEnabled('proj-1'), {
      wrapper: createWrapper(),
    })

    result.current.mutate({ workflowId: 'wf-1', enabled: true })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mock).toHaveBeenCalledWith({
      path: { workflowId: 'wf-1' },
      body: { projectId: 'proj-1', enabled: true },
    })
  })

  it('calls update API with enabled: false to disable a workflow', async () => {
    const mock = Workflows.putApiWorkflowsByWorkflowId as Mock
    mock.mockResolvedValueOnce({ data: { ...mockWorkflowDefinition, enabled: false } })

    const { result } = renderHook(() => useToggleWorkflowEnabled('proj-1'), {
      wrapper: createWrapper(),
    })

    result.current.mutate({ workflowId: 'wf-1', enabled: false })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mock).toHaveBeenCalledWith({
      path: { workflowId: 'wf-1' },
      body: { projectId: 'proj-1', enabled: false },
    })
  })

  it('handles toggle error', async () => {
    const mock = Workflows.putApiWorkflowsByWorkflowId as Mock
    mock.mockResolvedValueOnce({ error: { detail: 'Server error' } })

    const { result } = renderHook(() => useToggleWorkflowEnabled('proj-1'), {
      wrapper: createWrapper(),
    })

    result.current.mutate({ workflowId: 'wf-1', enabled: true })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })
})

describe('useCreateWorkflow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates a workflow successfully', async () => {
    const mock = Workflows.postApiWorkflows as Mock
    mock.mockResolvedValueOnce({ data: mockCreatedWorkflow })

    const { result } = renderHook(() => useCreateWorkflow('proj-1'), {
      wrapper: createWrapper(),
    })

    result.current.mutate({ title: 'New Workflow', description: 'A new workflow' })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mock).toHaveBeenCalledWith({
      body: { projectId: 'proj-1', title: 'New Workflow', description: 'A new workflow' },
    })
    expect(result.current.data).toEqual(mockCreatedWorkflow)
  })

  it('handles creation error', async () => {
    const mock = Workflows.postApiWorkflows as Mock
    mock.mockResolvedValueOnce({ error: { detail: 'Bad request' } })

    const { result } = renderHook(() => useCreateWorkflow('proj-1'), {
      wrapper: createWrapper(),
    })

    result.current.mutate({ title: '', description: '' })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })
})

describe('useWorkflowTemplates', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches templates successfully', async () => {
    const mock = WorkflowTemplate.getApiWorkflowTemplates as Mock
    mock.mockResolvedValueOnce({ data: mockTemplates })

    const { result } = renderHook(() => useWorkflowTemplates(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.templates).toEqual(mockTemplates)
  })

  it('handles fetch error', async () => {
    const mock = WorkflowTemplate.getApiWorkflowTemplates as Mock
    mock.mockResolvedValueOnce({ error: { detail: 'Server error' } })

    const { result } = renderHook(() => useWorkflowTemplates(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })
})

describe('useCreateFromTemplate', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates a workflow from template', async () => {
    const mock = WorkflowTemplate.postApiWorkflowTemplatesByTemplateIdCreate as Mock
    mock.mockResolvedValueOnce({ data: mockCreatedWorkflow })

    const { result } = renderHook(() => useCreateFromTemplate('proj-1'), {
      wrapper: createWrapper(),
    })

    result.current.mutate('tpl-1')

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mock).toHaveBeenCalledWith({
      path: { templateId: 'tpl-1' },
      query: { projectId: 'proj-1' },
    })
    expect(result.current.data).toEqual(mockCreatedWorkflow)
  })

  it('handles creation from template error', async () => {
    const mock = WorkflowTemplate.postApiWorkflowTemplatesByTemplateIdCreate as Mock
    mock.mockResolvedValueOnce({ error: { detail: 'Template not found' } })

    const { result } = renderHook(() => useCreateFromTemplate('proj-1'), {
      wrapper: createWrapper(),
    })

    result.current.mutate('tpl-missing')

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })
})
