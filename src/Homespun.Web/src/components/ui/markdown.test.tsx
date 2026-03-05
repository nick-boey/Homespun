import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Markdown } from './markdown'

describe('Markdown', () => {
  describe('heading rendering', () => {
    it('renders h1 headings with proper styling', () => {
      render(<Markdown># This is an H1 heading</Markdown>)

      const heading = screen.getByRole('heading', { level: 1 })
      expect(heading).toBeInTheDocument()
      expect(heading).toHaveTextContent('This is an H1 heading')

      // Typography plugin should add prose heading classes
      const container = heading.closest('.prose')
      expect(container).toBeInTheDocument()
      expect(container).toHaveClass('prose')
    })

    it('renders h2 headings with proper styling', () => {
      render(<Markdown>## This is an H2 heading</Markdown>)

      const heading = screen.getByRole('heading', { level: 2 })
      expect(heading).toBeInTheDocument()
      expect(heading).toHaveTextContent('This is an H2 heading')
    })

    it('renders h3 headings with proper styling', () => {
      render(<Markdown>### This is an H3 heading</Markdown>)

      const heading = screen.getByRole('heading', { level: 3 })
      expect(heading).toBeInTheDocument()
      expect(heading).toHaveTextContent('This is an H3 heading')
    })

    it('renders multiple heading levels correctly', () => {
      const markdown = `# H1 Heading
## H2 Heading
### H3 Heading
#### H4 Heading
##### H5 Heading
###### H6 Heading`

      render(<Markdown>{markdown}</Markdown>)

      expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('H1 Heading')
      expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('H2 Heading')
      expect(screen.getByRole('heading', { level: 3 })).toHaveTextContent('H3 Heading')
      expect(screen.getByRole('heading', { level: 4 })).toHaveTextContent('H4 Heading')
      expect(screen.getByRole('heading', { level: 5 })).toHaveTextContent('H5 Heading')
      expect(screen.getByRole('heading', { level: 6 })).toHaveTextContent('H6 Heading')
    })
  })

  describe('prose class application', () => {
    it('applies prose classes to container', () => {
      render(<Markdown className="prose prose-sm">Some content</Markdown>)

      const container = screen.getByText('Some content').closest('div')
      expect(container).toHaveClass('prose', 'prose-sm')
    })

    it('supports dark mode with prose-invert', () => {
      render(<Markdown className="prose prose-sm prose-invert">Dark mode content</Markdown>)

      const container = screen.getByText('Dark mode content').closest('div')
      expect(container).toHaveClass('prose', 'prose-sm', 'prose-invert')
    })
  })

  describe('responsive prose classes', () => {
    it('applies responsive prose classes', () => {
      render(<Markdown className="prose-sm md:prose">Responsive content</Markdown>)

      const container = screen.getByText('Responsive content').closest('div')
      expect(container).toHaveClass('prose-sm', 'md:prose')
    })
  })

  describe('text formatting', () => {
    it('renders paragraphs', () => {
      render(<Markdown>This is a paragraph of text.</Markdown>)

      expect(screen.getByText('This is a paragraph of text.')).toBeInTheDocument()
    })

    it('renders bold text', () => {
      render(<Markdown>This is **bold** text</Markdown>)

      const boldText = screen.getByText('bold')
      expect(boldText.tagName).toBe('STRONG')
    })

    it('renders italic text', () => {
      render(<Markdown>This is *italic* text</Markdown>)

      const italicText = screen.getByText('italic')
      expect(italicText.tagName).toBe('EM')
    })

    it('renders links', () => {
      render(<Markdown>This is [a link](https://example.com)</Markdown>)

      const link = screen.getByRole('link', { name: 'a link' })
      expect(link).toHaveAttribute('href', 'https://example.com')
    })
  })
})
