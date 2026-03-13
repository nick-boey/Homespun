import { useMemo } from 'react'

export interface SearchableItem {
  /** Unique identifier for the item */
  id: string
  /** Text displayed in results and used for matching */
  displayText: string
  /** Optional metadata for additional matching or display */
  metadata?: Record<string, unknown>
}

/**
 * Calculates a match score for an item against a query.
 * Higher scores indicate better matches.
 */
function calculateScore(item: SearchableItem, query: string, queryLower: string): number {
  const displayLower = item.displayText.toLowerCase()
  let score = 0

  // Exact match in display text
  if (displayLower === queryLower) {
    score += 100
  }

  // Check for exact substring match
  if (displayLower.includes(queryLower)) {
    score += 50

    // Bonus for match at start
    if (displayLower.startsWith(queryLower)) {
      score += 20
    }

    // Bonus for filename match (last path segment)
    const filename = item.displayText.split('/').pop()?.toLowerCase() || ''
    if (filename.includes(queryLower)) {
      score += 15
      if (filename === queryLower) {
        score += 30
      }
    }
  }

  // Check metadata fields (e.g., PR number, title)
  if (item.metadata) {
    const number = item.metadata.number
    const title = item.metadata.title as string | undefined

    // PR number exact match gets highest priority
    if (number !== undefined && String(number) === query) {
      score += 200
    } else if (number !== undefined && String(number).includes(query)) {
      score += 50
    }

    // Title match
    if (title) {
      const titleLower = title.toLowerCase()
      if (titleLower.includes(queryLower)) {
        score += 40
      }
    }
  }

  return score
}

/**
 * Performs fuzzy search on a list of items.
 * Returns items that match the query, sorted by relevance score.
 *
 * @param items - Array of searchable items
 * @param query - Search query string
 * @param maxResults - Maximum number of results to return (default: 20)
 * @returns Filtered and sorted array of matching items
 */
export function fuzzySearch(
  items: SearchableItem[],
  query: string,
  maxResults: number = 20
): SearchableItem[] {
  if (!items.length) {
    return []
  }

  // Return all items (up to maxResults) for empty query
  if (!query.trim()) {
    return items.slice(0, maxResults)
  }

  const queryLower = query.toLowerCase()

  // Filter items that match the query
  const matches: Array<{ item: SearchableItem; score: number }> = []

  for (const item of items) {
    const displayLower = item.displayText.toLowerCase()
    let isMatch = displayLower.includes(queryLower)

    // Also check metadata for PRs
    if (!isMatch && item.metadata) {
      const number = item.metadata.number
      const title = item.metadata.title as string | undefined

      if (number !== undefined && String(number).includes(query)) {
        isMatch = true
      }
      if (title && title.toLowerCase().includes(queryLower)) {
        isMatch = true
      }
    }

    if (isMatch) {
      const score = calculateScore(item, query, queryLower)
      matches.push({ item, score })
    }
  }

  // Sort by score descending, then by display text alphabetically
  matches.sort((a, b) => {
    if (b.score !== a.score) {
      return b.score - a.score
    }
    return a.item.displayText.localeCompare(b.item.displayText)
  })

  // Return top results
  return matches.slice(0, maxResults).map((m) => m.item)
}

/**
 * React hook for fuzzy search with memoization.
 *
 * @param items - Array of searchable items
 * @param query - Search query string
 * @param maxResults - Maximum number of results to return (default: 20)
 * @returns Memoized array of matching items
 */
export function useFuzzySearch(
  items: SearchableItem[],
  query: string,
  maxResults: number = 20
): SearchableItem[] {
  return useMemo(() => fuzzySearch(items, query, maxResults), [items, query, maxResults])
}
