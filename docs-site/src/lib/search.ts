import Fuse from 'fuse.js'
import { type DocPage, docPages, loadDocContent } from './docs'

export interface SearchResult {
  page: DocPage
  excerpt: string
}

let fuseInstance: Fuse<{ page: DocPage; content: string }> | null = null
let indexedData: { page: DocPage; content: string }[] = []

export async function initSearchIndex(): Promise<void> {
  if (fuseInstance) return
  const entries = await Promise.all(
    docPages.map(async (page) => {
      const content = (await loadDocContent(page.slug)) ?? ''
      return { page, content }
    })
  )
  indexedData = entries
  fuseInstance = new Fuse(indexedData, {
    keys: [
      { name: 'page.title', weight: 2 },
      { name: 'page.description', weight: 1.5 },
      { name: 'content', weight: 1 },
    ],
    threshold: 0.3,
    includeMatches: true,
    minMatchCharLength: 2,
  })
}

export function search(query: string): SearchResult[] {
  if (!fuseInstance || !query.trim()) return []
  const results = fuseInstance.search(query, { limit: 10 })
  return results.map((r) => {
    let excerpt = ''
    const contentMatch = r.matches?.find((m) => m.key === 'content')
    if (contentMatch?.indices?.[0]) {
      const [start] = contentMatch.indices[0]
      const contextStart = Math.max(0, start - 40)
      const contextEnd = Math.min(r.item.content.length, start + 100)
      excerpt = r.item.content.slice(contextStart, contextEnd).replace(/\n/g, ' ').trim()
      if (contextStart > 0) excerpt = '...' + excerpt
      if (contextEnd < r.item.content.length) excerpt = excerpt + '...'
    } else {
      excerpt = r.item.page.description
    }
    return { page: r.item.page, excerpt }
  })
}
