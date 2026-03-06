import { useMemo } from 'react'
import { useQueries } from '@tanstack/react-query'
import { useSessions } from './use-sessions'
import { useProjects } from '@/features/projects'
import { Issues, PullRequests } from '@/api'
import type { SessionSummary } from '@/api/generated/types.gen'
import type { EntityType } from './use-entity-info'
import type { IssueResponse, PullRequest } from '@/api/generated/types.gen'

export interface EnrichedSession {
  session: SessionSummary
  entityTitle?: string
  entityType?: EntityType
  projectName?: string
  messageCount: number
}

function detectEntityType(entityId: string): EntityType {
  // Detect entity type based on ID format
  if (entityId.toLowerCase().startsWith('pr-')) {
    return 'pr'
  }
  // Default to issue for other formats including 'issue-' prefix
  return 'issue'
}

export function useEnrichedSessions() {
  const { data: sessions, isLoading, isError, error, refetch } = useSessions()
  const { data: projects } = useProjects()

  // Create a map for quick project lookup
  const projectMap = useMemo(() => {
    if (!projects) return new Map()
    return new Map(projects.map((p) => [p.id, p]))
  }, [projects])

  // Get unique entity IDs
  const entityIds = useMemo(() => {
    if (!sessions) return []
    return Array.from(new Set(sessions.map((s) => s.entityId).filter(Boolean))) as string[]
  }, [sessions])

  // Batch fetch entity info for all unique entity IDs
  const entityInfoQueries = useQueries({
    queries: entityIds.map((entityId) => {
      const entityType = detectEntityType(entityId)
      return {
        queryKey: ['entity-info', entityId],
        queryFn: async () => {
          try {
            if (entityType === 'pr') {
              const response = await PullRequests.getApiPullRequestsById({
                path: { id: entityId },
              })
              const pr = response.data as PullRequest
              return {
                type: 'pr' as EntityType,
                title: pr.title || entityId,
                id: pr.id || entityId,
              }
            } else {
              const response = await Issues.getApiIssuesByIssueId({
                path: { issueId: entityId },
              })
              const issue = response.data as IssueResponse
              return {
                type: 'issue' as EntityType,
                title: issue.title || entityId,
                id: issue.id || entityId,
              }
            }
          } catch (error) {
            // If the API call fails, we'll return a basic info object
            // This allows the UI to still function even if the entity isn't found
            console.error(`Failed to fetch entity info for ${entityId}:`, error)
            return null
          }
        },
        staleTime: 5 * 60 * 1000, // Cache for 5 minutes
        gcTime: 10 * 60 * 1000, // Keep in cache for 10 minutes
        retry: 1, // Only retry once on failure
      }
    }),
  })

  // Create a map for quick entity info lookup
  const entityInfoMap = useMemo(() => {
    const map = new Map<string, { title: string; type: EntityType }>()
    entityIds.forEach((entityId, index) => {
      const query = entityInfoQueries[index]
      if (query?.data) {
        map.set(entityId, {
          title: query.data.title,
          type: query.data.type,
        })
      }
    })
    return map
  }, [entityIds, entityInfoQueries])

  // Enrich sessions with entity and project information
  const enrichedSessions = useMemo(() => {
    if (!sessions) return []

    return sessions.map((session): EnrichedSession => {
      const entityInfo = session.entityId ? entityInfoMap.get(session.entityId) : undefined
      const project = session.projectId ? projectMap.get(session.projectId) : undefined

      return {
        session,
        entityTitle: entityInfo?.title,
        entityType: entityInfo?.type,
        projectName: project?.name,
        messageCount: session.messageCount ?? 0,
      }
    })
  }, [sessions, entityInfoMap, projectMap])

  // Group sessions by project
  const groupedByProject = useMemo(() => {
    const groups = new Map<string, EnrichedSession[]>()

    enrichedSessions.forEach((enrichedSession) => {
      const projectId = enrichedSession.session.projectId || 'no-project'
      const existing = groups.get(projectId) || []
      groups.set(projectId, [...existing, enrichedSession])
    })

    return groups
  }, [enrichedSessions])

  return {
    sessions: enrichedSessions,
    groupedByProject,
    isLoading,
    isError,
    error,
    refetch,
  }
}
