export interface DocPage {
  slug: string
  title: string
  description: string
  section: string
  order: number
}

export interface NavSection {
  title: string
  items: DocPage[]
}

export const docPages: DocPage[] = [
  {
    slug: 'installation',
    title: 'Installation',
    description: 'Install and set up Homespun with Docker',
    section: 'Getting Started',
    order: 0,
  },
  {
    slug: 'usage',
    title: 'Usage Guide',
    description: 'Day-to-day operations and workflows',
    section: 'Getting Started',
    order: 1,
  },
  {
    slug: 'multi-user',
    title: 'Multi-User Setup',
    description: 'Configure multiple user instances',
    section: 'Guides',
    order: 2,
  },
  {
    slug: 'troubleshooting',
    title: 'Troubleshooting',
    description: 'Common issues and solutions',
    section: 'Guides',
    order: 3,
  },
]

export function getNavSections(): NavSection[] {
  const sectionMap = new Map<string, DocPage[]>()
  for (const page of docPages) {
    const items = sectionMap.get(page.section) ?? []
    items.push(page)
    sectionMap.set(page.section, items)
  }
  return Array.from(sectionMap.entries()).map(([title, items]) => ({
    title,
    items: items.sort((a, b) => a.order - b.order),
  }))
}

const markdownModules = import.meta.glob('/docs/*.md', {
  query: '?raw',
  import: 'default',
}) as Record<string, () => Promise<string>>

export async function loadDocContent(slug: string): Promise<string | null> {
  const key = `/docs/${slug}.md`
  const loader = markdownModules[key]
  if (!loader) return null
  return (await loader()) as string
}

export function extractHeadings(markdown: string): { id: string; text: string; level: number }[] {
  const headingRegex = /^(#{2,3})\s+(.+)$/gm
  const headings: { id: string; text: string; level: number }[] = []
  let match
  while ((match = headingRegex.exec(markdown)) !== null) {
    const level = match[1].length
    const text = match[2].trim()
    const id = text
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '')
    headings.push({ id, text, level })
  }
  return headings
}
