import { useQuery } from '@tanstack/react-query'
import { Issues, PullRequests } from '@/api'
import type { IssueResponse, PullRequest } from '@/api/generated/types.gen'

export type EntityType = 'issue' | 'pr' | 'unknown'

export interface EntityInfo {
  type: EntityType
  title: string
  id: string
}

export function detectEntityType(entityId: string): EntityType {
  const lower = entityId.toLowerCase()
  if (lower.startsWith('pr-')) {
    return 'pr'
  }
  if (lower.startsWith('issues-agent-') || lower.startsWith('rebase-')) {
    return 'unknown'
  }
  // Default to issue for other formats including 'issue-' prefix
  return 'issue'
}

export function useEntityInfo(entityId: string | null | undefined, projectId?: string) {
  const entityType = entityId ? detectEntityType(entityId) : null
  const queryKey =
    entityType === 'issue' && projectId
      ? ['entity-info', entityId, projectId]
      : ['entity-info', entityId]

  return useQuery({
    queryKey,
    queryFn: async (): Promise<EntityInfo | null> => {
      if (!entityId) return null

      const entityType = detectEntityType(entityId)
      if (entityType === 'unknown') return null

      try {
        if (entityType === 'pr') {
          const response = await PullRequests.getApiPullRequestsById({
            path: { id: entityId },
          })
          const pr = response.data as PullRequest | undefined
          return {
            type: 'pr',
            title: pr?.title || entityId,
            id: pr?.id || entityId,
          }
        } else {
          const response = await Issues.getApiIssuesByIssueId({
            path: { issueId: entityId },
            query: projectId ? { projectId } : undefined,
          })
          const issue = response.data as IssueResponse | undefined
          return {
            type: 'issue',
            title: issue?.title || entityId,
            id: issue?.id || entityId,
          }
        }
      } catch (error) {
        console.error(`Failed to fetch entity info for ${entityId}:`, error)
        throw error
      }
    },
    enabled: !!entityId && detectEntityType(entityId) !== 'unknown',
    staleTime: 5 * 60 * 1000, // Cache for 5 minutes
    gcTime: 10 * 60 * 1000, // Keep in cache for 10 minutes
    retry: 1, // Only retry once on failure
  })
}
