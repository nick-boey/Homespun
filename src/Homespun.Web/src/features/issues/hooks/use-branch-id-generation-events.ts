import { useEffect, useCallback } from 'react'
import { toast } from 'sonner'
import { useQueryClient } from '@tanstack/react-query'
import { useNotificationHub } from '@/providers/signalr-provider'
import { useBranchIdGenerationStore } from '../stores/branch-id-generation-store'
import { issueQueryKey } from './use-issue'
import { taskGraphQueryKey } from './use-task-graph'
import type { IssueResponse } from '@/api'

export interface UseBranchIdGenerationEventsOptions {
  /** Project ID to filter events for */
  projectId: string
}

/**
 * Hook that listens for branch ID generation SignalR events.
 * Handles:
 * - BranchIdGenerated events -> updates store, invalidates queries, shows toast
 * - BranchIdGenerationFailed events -> updates store, shows error toast
 */
export function useBranchIdGenerationEvents(options: UseBranchIdGenerationEventsOptions) {
  const { projectId } = options
  const { connection, isConnected } = useNotificationHub()
  const queryClient = useQueryClient()

  const markComplete = useBranchIdGenerationStore((state) => state.markComplete)

  // Handle BranchIdGenerated event
  const handleBranchIdGenerated = useCallback(
    (issueId: string, eventProjectId: string, branchId: string, wasAiGenerated: boolean) => {
      // Filter by project ID
      if (eventProjectId !== projectId) {
        return
      }

      // Update store to stop showing loading indicator
      markComplete(issueId)

      // Update issue cache directly to avoid triggering a refetch that would
      // overwrite user's in-progress form edits
      queryClient.setQueryData(
        issueQueryKey(issueId, eventProjectId),
        (oldData: IssueResponse | undefined) => {
          if (!oldData) return oldData
          return { ...oldData, workingBranchId: branchId }
        }
      )
      queryClient.invalidateQueries({
        queryKey: taskGraphQueryKey(eventProjectId),
      })

      // Show success toast
      toast.success('Branch ID generated', {
        description: wasAiGenerated
          ? `AI generated: ${branchId}`
          : `Generated from title: ${branchId}`,
      })
    },
    [projectId, markComplete, queryClient]
  )

  // Handle BranchIdGenerationFailed event
  const handleBranchIdGenerationFailed = useCallback(
    (issueId: string, eventProjectId: string, error: string) => {
      // Filter by project ID
      if (eventProjectId !== projectId) {
        return
      }

      // Update store to stop showing loading indicator
      markComplete(issueId)

      // Show error toast
      toast.error('Branch ID generation failed', {
        description: error,
      })
    },
    [projectId, markComplete]
  )

  // Register/unregister event handlers
  useEffect(() => {
    if (!connection || !isConnected) return

    connection.on('BranchIdGenerated', handleBranchIdGenerated)
    connection.on('BranchIdGenerationFailed', handleBranchIdGenerationFailed)

    return () => {
      connection.off('BranchIdGenerated', handleBranchIdGenerated)
      connection.off('BranchIdGenerationFailed', handleBranchIdGenerationFailed)
    }
  }, [connection, isConnected, handleBranchIdGenerated, handleBranchIdGenerationFailed])
}
