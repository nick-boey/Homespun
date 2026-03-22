import { describe, it, expect } from 'vitest'
import { detectMentionTrigger } from './use-mention-trigger'

describe('detectMentionTrigger', () => {
  describe('file trigger (@)', () => {
    it('detects @ at start of input', () => {
      const result = detectMentionTrigger('@', 1)
      expect(result).toEqual({
        active: true,
        type: '@',
        query: '',
        triggerPosition: 0,
      })
    })

    it('detects @ with query', () => {
      const result = detectMentionTrigger('@src', 4)
      expect(result).toEqual({
        active: true,
        type: '@',
        query: 'src',
        triggerPosition: 0,
      })
    })

    it('detects @ after space', () => {
      const result = detectMentionTrigger('hello @utils', 12)
      expect(result).toEqual({
        active: true,
        type: '@',
        query: 'utils',
        triggerPosition: 6,
      })
    })

    it('detects @ with path query', () => {
      const result = detectMentionTrigger('@src/components/bu', 18)
      expect(result).toEqual({
        active: true,
        type: '@',
        query: 'src/components/bu',
        triggerPosition: 0,
      })
    })

    it('detects @ at cursor in middle of text', () => {
      const result = detectMentionTrigger('See @config for details', 11)
      expect(result).toEqual({
        active: true,
        type: '@',
        query: 'config',
        triggerPosition: 4,
      })
    })
  })

  describe('PR trigger (#)', () => {
    it('detects # at start of input', () => {
      const result = detectMentionTrigger('#', 1)
      expect(result).toEqual({
        active: true,
        type: '#',
        query: '',
        triggerPosition: 0,
      })
    })

    it('detects # with number query', () => {
      const result = detectMentionTrigger('#123', 4)
      expect(result).toEqual({
        active: true,
        type: '#',
        query: '123',
        triggerPosition: 0,
      })
    })

    it('detects # with text query', () => {
      const result = detectMentionTrigger('PR #feature', 11)
      expect(result).toEqual({
        active: true,
        type: '#',
        query: 'feature',
        triggerPosition: 3,
      })
    })

    it('detects # after newline', () => {
      const result = detectMentionTrigger('line1\n#fix', 10)
      expect(result).toEqual({
        active: true,
        type: '#',
        query: 'fix',
        triggerPosition: 6,
      })
    })
  })

  describe('no trigger', () => {
    it('returns inactive for empty string', () => {
      const result = detectMentionTrigger('', 0)
      expect(result.active).toBe(false)
    })

    it('returns inactive for plain text', () => {
      const result = detectMentionTrigger('hello world', 11)
      expect(result.active).toBe(false)
    })

    it('returns inactive when cursor is before trigger', () => {
      const result = detectMentionTrigger('hello @file', 3)
      expect(result.active).toBe(false)
    })

    it('returns inactive for email-like @ with no space before', () => {
      const result = detectMentionTrigger('test@example.com', 16)
      expect(result.active).toBe(false)
    })

    it('returns inactive when @ reference is followed by space (unquoted path)', () => {
      // After typing a file path and adding a space, search should end
      const result = detectMentionTrigger('@src/utils.ts ', 14)
      expect(result.active).toBe(false)
    })

    it('returns inactive when quoted @ reference is complete', () => {
      // Quoted path with closing quote means reference is complete
      const result = detectMentionTrigger('@"src/file name.ts" ', 20)
      expect(result.active).toBe(false)
    })

    it('returns inactive when # is followed by space', () => {
      // Space immediately after # should deactivate
      const result = detectMentionTrigger('# 123', 5)
      expect(result.active).toBe(false)
    })

    it('returns inactive when cursor is in middle of word after @', () => {
      // If cursor is inside an already typed word (not at end), don't trigger
      const result = detectMentionTrigger('@filename', 5)
      // Still active as cursor is in the query
      expect(result.active).toBe(true)
      expect(result.query).toBe('file')
    })
  })

  describe('edge cases', () => {
    it('handles multiple @ symbols - uses closest to cursor', () => {
      const result = detectMentionTrigger('See @file1 and @file2', 21)
      expect(result).toEqual({
        active: true,
        type: '@',
        query: 'file2',
        triggerPosition: 15,
      })
    })

    it('handles mixed @ and # - uses closest to cursor', () => {
      const result = detectMentionTrigger('@file #pr', 9)
      expect(result).toEqual({
        active: true,
        type: '#',
        query: 'pr',
        triggerPosition: 6,
      })
    })

    it('handles @ after #', () => {
      const result = detectMentionTrigger('#123 @file', 10)
      expect(result).toEqual({
        active: true,
        type: '@',
        query: 'file',
        triggerPosition: 5,
      })
    })
  })

  describe('file trigger completion (new format)', () => {
    it('remains active for @filepath without trailing space', () => {
      const result = detectMentionTrigger('@src/file.ts', 12)
      expect(result.active).toBe(true)
      expect(result.query).toBe('src/file.ts')
    })

    it('becomes inactive after whitespace for @filepath', () => {
      const result = detectMentionTrigger('@src/file.ts ', 13)
      expect(result.active).toBe(false)
    })

    it('becomes inactive after tab for @filepath', () => {
      const result = detectMentionTrigger('@src/file.ts\t', 13)
      expect(result.active).toBe(false)
    })

    it('becomes inactive after newline for @filepath', () => {
      const result = detectMentionTrigger('@src/file.ts\nmore text', 13)
      expect(result.active).toBe(false)
    })

    it('remains active inside quoted path with spaces', () => {
      // Unclosed quote means we're still typing
      const result = detectMentionTrigger('@"src/my file', 13)
      expect(result.active).toBe(true)
      expect(result.query).toBe('"src/my file')
    })

    it('becomes inactive when quoted path is closed', () => {
      const result = detectMentionTrigger('@"src/my file.ts"', 17)
      expect(result.active).toBe(false)
    })

    it('remains active for partial path before space', () => {
      // Cursor is right after "ts" before the space
      const result = detectMentionTrigger('@src/file.ts more', 12)
      expect(result.active).toBe(true)
      expect(result.query).toBe('src/file.ts')
    })
  })

  describe('PR trigger completion (no spaces)', () => {
    it('becomes inactive when space follows #', () => {
      // Space immediately after # should deactivate trigger
      const result = detectMentionTrigger('# 123', 2)
      expect(result.active).toBe(false)
    })

    it('stays active for #123 without space', () => {
      const result = detectMentionTrigger('#123', 4)
      expect(result.active).toBe(true)
      expect(result.query).toBe('123')
    })

    it('becomes inactive when cursor is after space in # query', () => {
      // Cursor is after the space following #
      const result = detectMentionTrigger('See # more', 6)
      expect(result.active).toBe(false)
    })

    it('stays active for # at end of input', () => {
      const result = detectMentionTrigger('See #', 5)
      expect(result.active).toBe(true)
      expect(result.query).toBe('')
    })
  })
})
