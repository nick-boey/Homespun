import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { TableOfContents } from './TableOfContents'

const headings = [
  { id: 'installation', text: 'Installation', level: 2 },
  { id: 'prerequisites', text: 'Prerequisites', level: 3 },
  { id: 'configuration', text: 'Configuration', level: 2 },
]

describe('TableOfContents', () => {
  it('renders heading links', () => {
    render(<TableOfContents headings={headings} />)
    expect(screen.getByText('Installation')).toBeInTheDocument()
    expect(screen.getByText('Prerequisites')).toBeInTheDocument()
    expect(screen.getByText('Configuration')).toBeInTheDocument()
  })

  it('renders nothing when headings is empty', () => {
    const { container } = render(<TableOfContents headings={[]} />)
    expect(container.innerHTML).toBe('')
  })

  it('renders heading links with correct href', () => {
    render(<TableOfContents headings={headings} />)
    const link = screen.getByText('Installation')
    expect(link.getAttribute('href')).toBe('#installation')
  })

  it('highlights the active heading', () => {
    render(<TableOfContents headings={headings} activeId="configuration" />)
    const activeLink = screen.getByText('Configuration')
    expect(activeLink.className).toContain('font-medium')
  })
})
