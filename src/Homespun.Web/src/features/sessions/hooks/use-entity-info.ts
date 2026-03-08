import { useQuery } from '@tanstack/react-query'
import { Issues, PullRequests } from '@/api'
import type { IssueResponse, PullRequest } from '@/api/generated/types.gen'

export type EntityType = 'issue' | 'pr'

export interface EntityInfo {
  type: EntityType
  title: string
  id: string
}

function detectEntityType(entityId: string): EntityType {
  // Detect entity type based on ID format
  if (entityId.toLowerCase().startsWith('pr-')) {
    return 'pr'
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

      try {
        if (entityType === 'pr') {
          const response = await PullRequests.getApiPullRequestsById({
            path: { id: entityId },
          })
          const pr = response.data as PullRequest
          return {
            type: 'pr',
            title: pr.title || entityId,
            id: pr.id || entityId,
          }
        } else {
          const response = await Issues.getApiIssuesByIssueId({
            path: { issueId: entityId },
            query: projectId ? { projectId } : undefined,
          })
          const issue = response.data as IssueResponse
          return {
            type: 'issue',
            title: issue.title || entityId,
            id: issue.id || entityId,
          }
        }
      } catch (error) {
        // If the API call fails, we'll return a basic info object
        // This allows the UI to still function even if the entity isn't found
        console.error(`Failed to fetch entity info for ${entityId}:`, error)
        throw error
      }
    },
    enabled: !!entityId,
    staleTime: 5 * 60 * 1000, // Cache for 5 minutes
    gcTime: 10 * 60 * 1000, // Keep in cache for 10 minutes
    retry: 1, // Only retry once on failure
  })
}
