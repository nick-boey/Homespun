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

  // Get unique entity IDs with their project context
  const entityQueries = useMemo(() => {
    if (!sessions) return []

    // Create a map to track unique entity+project combinations
    const uniqueQueries = new Map<string, { entityId: string; projectId?: string }>()

    sessions.forEach((session) => {
      if (session.entityId) {
        // For issues, we need both entityId and projectId
        // For PRs, projectId is not needed
        const entityType = detectEntityType(session.entityId)
        const key =
          entityType === 'issue' && session.projectId
            ? `${session.entityId}:${session.projectId}`
            : session.entityId

        if (!uniqueQueries.has(key)) {
          uniqueQueries.set(key, {
            entityId: session.entityId,
            projectId: entityType === 'issue' ? (session.projectId ?? undefined) : undefined,
          })
        }
      }
    })

    return Array.from(uniqueQueries.values())
  }, [sessions])

  // Batch fetch entity info for all unique entity IDs
  const entityInfoQueries = useQueries({
    queries: entityQueries.map(({ entityId, projectId }) => {
      const entityType = detectEntityType(entityId)
      const queryKey =
        entityType === 'issue' && projectId
          ? ['entity-info', entityId, projectId]
          : ['entity-info', entityId]

      return {
        queryKey,
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
                query: projectId ? { projectId } : undefined,
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
    entityQueries.forEach(({ entityId, projectId }, index) => {
      const query = entityInfoQueries[index]
      if (query?.data) {
        // For issues with projectId, use composite key, otherwise just entityId
        const entityType = detectEntityType(entityId)
        const key = entityType === 'issue' && projectId ? `${entityId}:${projectId}` : entityId
        map.set(key, {
          title: query.data.title,
          type: query.data.type,
        })
      }
    })
    return map
  }, [entityQueries, entityInfoQueries])

  // Enrich sessions with entity and project information
  const enrichedSessions = useMemo(() => {
    if (!sessions) return []

    return sessions.map((session): EnrichedSession => {
      let entityInfo: { title: string; type: EntityType } | undefined

      if (session.entityId) {
        const entityType = detectEntityType(session.entityId)
        // For issues with projectId, use composite key, otherwise just entityId
        const key =
          entityType === 'issue' && session.projectId
            ? `${session.entityId}:${session.projectId}`
            : session.entityId
        entityInfo = entityInfoMap.get(key)
      }

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
