import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MarkdownContent } from './MarkdownContent'

describe('MarkdownContent', () => {
  it('renders markdown text', () => {
    render(<MarkdownContent content="Hello **world**" />)
    expect(screen.getByText('world')).toBeInTheDocument()
  })

  it('renders headings with ids', () => {
    render(<MarkdownContent content="## My Heading" />)
    const heading = screen.getByText('My Heading')
    expect(heading.id).toBe('my-heading')
  })

  it('renders code blocks', () => {
    const { container } = render(<MarkdownContent content={'```bash\nnpm install\n```'} />)
    const codeBlock = container.querySelector('code.language-bash')
    expect(codeBlock).toBeInTheDocument()
    expect(codeBlock?.textContent).toContain('npm')
    expect(codeBlock?.textContent).toContain('install')
  })

  it('renders inline code', () => {
    render(<MarkdownContent content="Use `npm install` to install" />)
    expect(screen.getByText('npm install')).toBeInTheDocument()
  })

  it('renders GFM tables', () => {
    const table = '| Col A | Col B |\n|-------|-------|\n| 1 | 2 |'
    render(<MarkdownContent content={table} />)
    expect(screen.getByText('Col A')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument()
  })

  it('wraps content in prose styling', () => {
    const { container } = render(<MarkdownContent content="test" />)
    const wrapper = container.querySelector('[data-testid="markdown-content"]')
    expect(wrapper?.className).toContain('prose')
  })
})
