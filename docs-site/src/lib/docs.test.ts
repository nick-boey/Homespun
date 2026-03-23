import { describe, it, expect } from 'vitest'
import { docPages, getNavSections, extractHeadings } from './docs'

describe('docPages', () => {
  it('contains all expected documentation pages', () => {
    const slugs = docPages.map((p) => p.slug)
    expect(slugs).toContain('installation')
    expect(slugs).toContain('usage')
    expect(slugs).toContain('multi-user')
    expect(slugs).toContain('troubleshooting')
  })

  it('has unique slugs', () => {
    const slugs = docPages.map((p) => p.slug)
    expect(new Set(slugs).size).toBe(slugs.length)
  })
})

describe('getNavSections', () => {
  it('returns sections with items', () => {
    const sections = getNavSections()
    expect(sections.length).toBeGreaterThan(0)
    for (const section of sections) {
      expect(section.title).toBeTruthy()
      expect(section.items.length).toBeGreaterThan(0)
    }
  })

  it('groups pages by section', () => {
    const sections = getNavSections()
    const gettingStarted = sections.find((s) => s.title === 'Getting Started')
    expect(gettingStarted).toBeDefined()
    expect(gettingStarted!.items.map((i) => i.slug)).toContain('installation')
  })

  it('sorts items within sections by order', () => {
    const sections = getNavSections()
    for (const section of sections) {
      for (let i = 1; i < section.items.length; i++) {
        expect(section.items[i].order).toBeGreaterThanOrEqual(section.items[i - 1].order)
      }
    }
  })
})

describe('extractHeadings', () => {
  it('extracts h2 and h3 headings from markdown', () => {
    const md = `# Title\n## Section One\nSome text\n### Subsection\n## Section Two`
    const headings = extractHeadings(md)
    expect(headings).toEqual([
      { id: 'section-one', text: 'Section One', level: 2 },
      { id: 'subsection', text: 'Subsection', level: 3 },
      { id: 'section-two', text: 'Section Two', level: 2 },
    ])
  })

  it('returns empty array for markdown with no headings', () => {
    expect(extractHeadings('Just some text')).toEqual([])
  })

  it('generates URL-safe IDs', () => {
    const md = '## Hello World & Friends!'
    const headings = extractHeadings(md)
    expect(headings[0].id).toBe('hello-world-friends')
  })
})
