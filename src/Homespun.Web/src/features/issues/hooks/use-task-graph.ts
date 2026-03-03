import { useQuery } from '@tanstack/react-query'
import { Graph, type TaskGraphResponse } from '@/api'

export const taskGraphQueryKey = (projectId: string) => ['taskGraph', projectId] as const

export interface UseTaskGraphOptions {
  /** Maximum number of past PRs to include (default: 5) */
  maxPastPRs?: number
}

export interface UseTaskGraphResult {
  taskGraph: TaskGraphResponse | undefined
  isLoading: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

/**
 * Hook for fetching task graph data from the API.
 *
 * @param projectId - The project ID to fetch the task graph for
 * @param options - Optional configuration
 * @returns Task graph data and query state
 *
 * @example
 * ```tsx
 * const { taskGraph, isLoading } = useTaskGraph(projectId)
 *
 * if (isLoading) return <Skeleton />
 * if (!taskGraph) return null
 *
 * return <TaskGraphView data={taskGraph} />
 * ```
 */
export function useTaskGraph(
  projectId: string,
  options: UseTaskGraphOptions = {}
): UseTaskGraphResult {
  const { maxPastPRs = 5 } = options

  const query = useQuery({
    queryKey: taskGraphQueryKey(projectId),
    queryFn: async () => {
      const response = await Graph.getApiGraphByProjectIdTaskgraphData({
        path: { projectId },
        query: { maxPastPRs },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch task graph')
      }

      return response.data
    },
    enabled: !!projectId,
  })

  return {
    taskGraph: query.data,
    isLoading: query.isLoading,
    isSuccess: query.isSuccess,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
