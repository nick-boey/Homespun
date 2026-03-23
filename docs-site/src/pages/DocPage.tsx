import { useEffect, useState } from 'react'
import { useParams, Navigate } from 'react-router-dom'
import { MarkdownContent } from '@/components/MarkdownContent'
import { TableOfContents } from '@/components/TableOfContents'
import { docPages, extractHeadings, loadDocContent } from '@/lib/docs'

export function DocPage() {
  const { slug } = useParams<{ slug: string }>()
  const page = docPages.find((p) => p.slug === slug)
  const [content, setContent] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  useEffect(() => {
    if (!slug || !page) return

    let cancelled = false

    loadDocContent(slug).then((md) => {
      if (!cancelled) {
        setContent(md)
        setLoading(false)
      }
    })

    return () => {
      cancelled = true
      setContent(null)
      setLoading(true)
    }
  }, [slug, page])

  if (!slug || !page) {
    return <Navigate to="/" replace />
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="text-muted-foreground">Loading...</div>
      </div>
    )
  }

  if (!content) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="text-muted-foreground">Page not found.</div>
      </div>
    )
  }

  const headings = extractHeadings(content)

  return (
    <div className="flex gap-8" data-testid="doc-page">
      <div className="min-w-0 flex-1">
        <MarkdownContent content={content} />
      </div>
      {headings.length > 0 && (
        <aside className="sticky top-14 hidden h-[calc(100vh-3.5rem)] w-48 shrink-0 overflow-y-auto py-6 xl:block">
          <TableOfContents headings={headings} />
        </aside>
      )}
    </div>
  )
}
