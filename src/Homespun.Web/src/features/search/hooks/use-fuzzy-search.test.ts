import { describe, it, expect } from 'vitest'
import { fuzzySearch, type SearchableItem } from './use-fuzzy-search'

describe('fuzzySearch', () => {
  describe('file search (@)', () => {
    const files: SearchableItem[] = [
      { id: 'src/index.ts', displayText: 'src/index.ts' },
      { id: 'src/utils/helpers.ts', displayText: 'src/utils/helpers.ts' },
      { id: 'src/components/Button.tsx', displayText: 'src/components/Button.tsx' },
      { id: 'src/components/Card.tsx', displayText: 'src/components/Card.tsx' },
      { id: 'package.json', displayText: 'package.json' },
      { id: 'README.md', displayText: 'README.md' },
    ]

    it('returns all items for empty query', () => {
      const result = fuzzySearch(files, '')
      expect(result).toHaveLength(6)
    })

    it('filters by exact substring match', () => {
      const result = fuzzySearch(files, 'Button')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('src/components/Button.tsx')
    })

    it('performs case-insensitive matching', () => {
      const result = fuzzySearch(files, 'button')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('src/components/Button.tsx')
    })

    it('matches partial path segments', () => {
      const result = fuzzySearch(files, 'comp')
      expect(result).toHaveLength(2)
      expect(result.map((r) => r.id)).toContain('src/components/Button.tsx')
      expect(result.map((r) => r.id)).toContain('src/components/Card.tsx')
    })

    it('matches by file extension', () => {
      const result = fuzzySearch(files, '.tsx')
      expect(result).toHaveLength(2)
    })

    it('matches full path', () => {
      const result = fuzzySearch(files, 'src/utils')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('src/utils/helpers.ts')
    })

    it('returns empty array when no match', () => {
      const result = fuzzySearch(files, 'nonexistent')
      expect(result).toHaveLength(0)
    })

    it('limits results to maxResults', () => {
      const manyFiles = Array.from({ length: 30 }, (_, i) => ({
        id: `file${i}.ts`,
        displayText: `file${i}.ts`,
      }))
      const result = fuzzySearch(manyFiles, 'file', 20)
      expect(result).toHaveLength(20)
    })

    it('scores exact filename matches higher', () => {
      const testFiles: SearchableItem[] = [
        { id: 'src/button/index.ts', displayText: 'src/button/index.ts' },
        { id: 'Button.tsx', displayText: 'Button.tsx' },
        { id: 'src/components/Button.tsx', displayText: 'src/components/Button.tsx' },
      ]
      const result = fuzzySearch(testFiles, 'Button')
      // Exact filename match should be first
      expect(result[0].id).toBe('Button.tsx')
    })
  })

  describe('PR search (#)', () => {
    const prs: SearchableItem[] = [
      {
        id: '123',
        displayText: '#123 - Add feature X',
        metadata: { number: 123, title: 'Add feature X' },
      },
      { id: '456', displayText: '#456 - Fix bug Y', metadata: { number: 456, title: 'Fix bug Y' } },
      {
        id: '789',
        displayText: '#789 - Update documentation',
        metadata: { number: 789, title: 'Update documentation' },
      },
      {
        id: '42',
        displayText: '#42 - Initial commit',
        metadata: { number: 42, title: 'Initial commit' },
      },
    ]

    it('matches PR number', () => {
      const result = fuzzySearch(prs, '123')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('123')
    })

    it('matches PR title', () => {
      const result = fuzzySearch(prs, 'feature')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('123')
    })

    it('matches partial title case-insensitively', () => {
      const result = fuzzySearch(prs, 'FIX')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('456')
    })

    it('scores exact number match higher than title match', () => {
      const testPrs: SearchableItem[] = [
        {
          id: '42',
          displayText: '#42 - Initial commit',
          metadata: { number: 42, title: 'Initial commit' },
        },
        {
          id: '142',
          displayText: '#142 - Bug 42 fix',
          metadata: { number: 142, title: 'Bug 42 fix' },
        },
      ]
      const result = fuzzySearch(testPrs, '42')
      expect(result[0].id).toBe('42')
    })
  })

  describe('edge cases', () => {
    it('handles empty items array', () => {
      const result = fuzzySearch([], 'test')
      expect(result).toHaveLength(0)
    })

    it('handles special regex characters in query', () => {
      const items: SearchableItem[] = [
        { id: 'file[1].ts', displayText: 'file[1].ts' },
        { id: 'test.ts', displayText: 'test.ts' },
      ]
      const result = fuzzySearch(items, '[1]')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('file[1].ts')
    })

    it('handles whitespace in query', () => {
      const items: SearchableItem[] = [
        { id: '1', displayText: '#1 - Add feature', metadata: { title: 'Add feature' } },
        { id: '2', displayText: '#2 - Remove feature', metadata: { title: 'Remove feature' } },
      ]
      const result = fuzzySearch(items, 'add feature')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('1')
    })
  })
})
