import { useIssue } from '@/features/issues/hooks/use-issue'

/**
 * Hook to fetch issue data based on an entity ID
 *
 * Entity ID formats:
 * - issue:issueId
 * - feature:featureId
 * - project:projectId (returns no issue data)
 *
 * @param entityId The entity ID to parse
 * @param projectId The project ID
 * @returns Issue data, loading state, and error
 */
export function useIssueByEntityId(entityId: string, projectId: string) {
  // Parse the entity ID to extract the issue ID
  const issueId = parseIssueIdFromEntityId(entityId)

  // Use the existing useIssue hook
  const result = useIssue(issueId || '', projectId || '')

  return {
    issue: result.issue,
    isLoading: result.isLoading,
    error: result.error,
  }
}

/**
 * Parse issue ID from entity ID string
 * @param entityId Entity ID in format "type:id"
 * @returns The ID portion if it's an issue or feature, empty string otherwise
 */
function parseIssueIdFromEntityId(entityId: string): string {
  if (!entityId) return ''

  const [type, id] = entityId.split(':')

  // Only return ID for issue and feature types
  if ((type === 'issue' || type === 'feature') && id) {
    return id
  }

  return ''
}
