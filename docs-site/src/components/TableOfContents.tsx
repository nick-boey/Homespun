import { cn } from '@/lib/utils'

interface Heading {
  id: string
  text: string
  level: number
}

interface TableOfContentsProps {
  headings: Heading[]
  activeId?: string
}

export function TableOfContents({ headings, activeId }: TableOfContentsProps) {
  if (headings.length === 0) return null

  return (
    <nav className="space-y-1" data-testid="table-of-contents">
      <h4 className="text-foreground mb-2 text-sm font-semibold">On this page</h4>
      {headings.map((heading) => (
        <a
          key={heading.id}
          href={`#${heading.id}`}
          className={cn(
            'hover:text-foreground block text-sm transition-colors',
            heading.level === 3 ? 'pl-4' : '',
            activeId === heading.id ? 'text-foreground font-medium' : 'text-muted-foreground'
          )}
        >
          {heading.text}
        </a>
      ))}
    </nav>
  )
}
