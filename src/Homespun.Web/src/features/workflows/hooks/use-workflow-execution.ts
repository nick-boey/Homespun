import { useEffect, useRef, useState } from 'react'
import { createHubConnection, startConnection, stopConnection } from '@/lib/signalr/connection'
import type { ConnectionStatus } from '@/types/signalr'
import type { StepExecutionStatus, WorkflowExecutionStatus } from '@/api/generated/types.gen'

export interface StepStatusInfo {
  status: StepExecutionStatus
  stepIndex: number
  retryCount?: number
  maxRetries?: number
  output?: Record<string, unknown> | null
  error?: string
}

export interface PendingGate {
  stepId: string
  gateName: string
}

export interface UseWorkflowExecutionResult {
  stepStatuses: Record<string, StepStatusInfo>
  workflowStatus: WorkflowExecutionStatus | null
  workflowError: string | null
  pendingGate: PendingGate | null
  connectionStatus: ConnectionStatus
}

const WORKFLOW_HUB_URL = '/hubs/workflows'

export function useWorkflowExecution(executionId: string): UseWorkflowExecutionResult {
  const [stepStatuses, setStepStatuses] = useState<Record<string, StepStatusInfo>>({})
  const [workflowStatus, setWorkflowStatus] = useState<WorkflowExecutionStatus | null>(null)
  const [workflowError, setWorkflowError] = useState<string | null>(null)
  const [pendingGate, setPendingGate] = useState<PendingGate | null>(null)
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected')
  const connectionRef = useRef<ReturnType<typeof createHubConnection> | null>(null)

  useEffect(() => {
    const connection = createHubConnection({
      hubUrl: WORKFLOW_HUB_URL,
      onStatusChange: (status) => {
        setConnectionStatus(status)
        if (status === 'connected') {
          connection.invoke('JoinExecution', executionId)
        }
      },
      onReconnected: () => {
        connection.invoke('JoinExecution', executionId)
      },
    })

    connectionRef.current = connection

    // Register event handlers
    connection.on('StepStarted', (_executionId: string, stepId: string, stepIndex: number) => {
      setStepStatuses((prev) => ({
        ...prev,
        [stepId]: { ...prev[stepId], status: 'running', stepIndex },
      }))
    })

    connection.on(
      'StepCompleted',
      (
        _executionId: string,
        stepId: string,
        status: StepExecutionStatus,
        output: Record<string, unknown> | null
      ) => {
        setStepStatuses((prev) => ({
          ...prev,
          [stepId]: { ...prev[stepId], status, output },
        }))
        setPendingGate((prev) => (prev?.stepId === stepId ? null : prev))
      }
    )

    connection.on('StepFailed', (_executionId: string, stepId: string, error: string) => {
      setStepStatuses((prev) => ({
        ...prev,
        [stepId]: { ...prev[stepId], status: 'failed', error },
      }))
    })

    connection.on(
      'StepRetrying',
      (_executionId: string, stepId: string, retryCount: number, maxRetries: number) => {
        setStepStatuses((prev) => ({
          ...prev,
          [stepId]: { ...prev[stepId], status: 'running', retryCount, maxRetries },
        }))
      }
    )

    connection.on('WorkflowCompleted', (_executionId: string, status: WorkflowExecutionStatus) => {
      setWorkflowStatus(status)
    })

    connection.on('WorkflowFailed', (_executionId: string, error: string) => {
      setWorkflowStatus('failed')
      setWorkflowError(error)
    })

    connection.on('GatePending', (_executionId: string, stepId: string, gateName: string) => {
      setPendingGate({ stepId, gateName })
    })

    startConnection(connection, (status) => {
      setConnectionStatus(status)
    })

    return () => {
      stopConnection(connection)
      connectionRef.current = null
    }
  }, [executionId])

  return {
    stepStatuses,
    workflowStatus,
    workflowError,
    pendingGate,
    connectionStatus,
  }
}
