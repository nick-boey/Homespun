import { describe, it, expect } from 'vitest'
import { serializePrompts, parsePrompts, calculateDiff, type ExportedPrompt } from './prompt-diff'
import { SessionMode, SessionType } from '@/api/generated/types.gen'
import type { AgentPrompt } from '@/api/generated/types.gen'

describe('serializePrompts', () => {
  it('handles empty array', () => {
    const result = serializePrompts([])
    expect(result).toBe('[]')
  })

  it('serializes prompts with correct fields', () => {
    const prompts: AgentPrompt[] = [
      {
        name: 'Test Prompt',
        initialMessage: 'Hello world',
        mode: SessionMode.BUILD,
        projectId: 'proj-1',
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-02T00:00:00Z',
      },
    ]

    const result = serializePrompts(prompts)
    const parsed = JSON.parse(result)

    expect(parsed).toHaveLength(1)
    expect(parsed[0]).toEqual({
      name: 'Test Prompt',
      initialMessage: 'Hello world',
      mode: SessionMode.BUILD,
    })
    // System fields should be excluded
    expect(parsed[0]).not.toHaveProperty('projectId')
    expect(parsed[0]).not.toHaveProperty('createdAt')
    expect(parsed[0]).not.toHaveProperty('updatedAt')
    expect(parsed[0]).not.toHaveProperty('id')
  })

  it('includes sessionType for Issue Agent Prompts', () => {
    const prompts: AgentPrompt[] = [
      {
        name: 'Issue Modify',
        initialMessage: 'Modify issues',
        mode: SessionMode.BUILD,
        sessionType: SessionType.ISSUE_AGENT_MODIFICATION,
      },
    ]

    const result = serializePrompts(prompts)
    const parsed = JSON.parse(result)

    expect(parsed[0]).toEqual({
      name: 'Issue Modify',
      initialMessage: 'Modify issues',
      mode: SessionMode.BUILD,
      sessionType: SessionType.ISSUE_AGENT_MODIFICATION,
    })
  })

  it('formats JSON with 2-space indentation', () => {
    const prompts: AgentPrompt[] = [
      {
        name: 'Test',
        mode: SessionMode.PLAN,
      },
    ]

    const result = serializePrompts(prompts)
    expect(result).toContain('\n')
    expect(result).toContain('  ')
  })

  it('handles prompts with null values', () => {
    const prompts: AgentPrompt[] = [
      {
        name: 'Test',
        initialMessage: null,
        mode: SessionMode.BUILD,
      },
    ]

    const result = serializePrompts(prompts)
    const parsed = JSON.parse(result)

    expect(parsed[0].initialMessage).toBeNull()
  })
})

describe('parsePrompts', () => {
  it('parses valid JSON array', () => {
    const json = JSON.stringify([
      {
        name: 'Test',
        mode: SessionMode.BUILD,
      },
    ])

    const result = parsePrompts(json)
    expect(result.success).toBe(true)
    if (result.success) {
      expect(result.data).toHaveLength(1)
      expect(result.data[0].name).toBe('Test')
    }
  })

  it('returns error for invalid JSON', () => {
    const result = parsePrompts('not valid json')
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error).toContain('Invalid JSON')
    }
  })

  it('returns error when not an array', () => {
    const result = parsePrompts('{ "name": "Test" }')
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error).toContain('must be an array')
    }
  })

  it('validates required fields - name is required', () => {
    const json = JSON.stringify([{ mode: SessionMode.BUILD }])

    const result = parsePrompts(json)
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error).toContain('name')
    }
  })

  it('validates required fields - mode is required', () => {
    const json = JSON.stringify([{ name: 'Test' }])

    const result = parsePrompts(json)
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error).toContain('mode')
    }
  })

  it('validates mode is a valid SessionMode', () => {
    const json = JSON.stringify([{ name: 'Test', mode: 'invalid' }])

    const result = parsePrompts(json)
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error).toContain('mode')
    }
  })

  it('handles empty array', () => {
    const result = parsePrompts('[]')
    expect(result.success).toBe(true)
    if (result.success) {
      expect(result.data).toEqual([])
    }
  })

  it('accepts prompts without extra fields', () => {
    const json = JSON.stringify([
      {
        name: 'New Prompt',
        mode: SessionMode.BUILD,
      },
    ])

    const result = parsePrompts(json)
    expect(result.success).toBe(true)
    if (result.success) {
      expect(result.data[0].name).toBe('New Prompt')
    }
  })

  it('validates sessionType is a valid SessionType', () => {
    const json = JSON.stringify([{ name: 'Test', mode: SessionMode.BUILD, sessionType: 'invalid' }])

    const result = parsePrompts(json)
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error).toContain('sessionType')
    }
  })

  it('accepts valid sessionType values', () => {
    const json = JSON.stringify([
      {
        name: 'Issue Modify',
        mode: SessionMode.BUILD,
        sessionType: SessionType.ISSUE_AGENT_MODIFICATION,
      },
    ])

    const result = parsePrompts(json)
    expect(result.success).toBe(true)
    if (result.success) {
      expect(result.data[0].sessionType).toBe(SessionType.ISSUE_AGENT_MODIFICATION)
    }
  })
})

describe('calculateDiff', () => {
  it('identifies prompts to create (name not in current)', () => {
    const current: ExportedPrompt[] = []
    const edited: ExportedPrompt[] = [{ name: 'New Prompt', mode: SessionMode.BUILD }]

    const diff = calculateDiff(current, edited)

    expect(diff.creates).toHaveLength(1)
    expect(diff.creates[0]).toEqual({
      name: 'New Prompt',
      mode: SessionMode.BUILD,
    })
    expect(diff.updates).toHaveLength(0)
    expect(diff.deletes).toHaveLength(0)
  })

  it('identifies prompts to update (matching name, changed content)', () => {
    const current: ExportedPrompt[] = [{ name: 'My Prompt', mode: SessionMode.BUILD }]
    const edited: ExportedPrompt[] = [{ name: 'My Prompt', mode: SessionMode.PLAN }]

    const diff = calculateDiff(current, edited)

    expect(diff.creates).toHaveLength(0)
    expect(diff.updates).toHaveLength(1)
    expect(diff.updates[0]).toEqual({
      name: 'My Prompt',
      mode: SessionMode.PLAN,
    })
    expect(diff.deletes).toHaveLength(0)
  })

  it('identifies prompts to delete (in current but not in edited)', () => {
    const current: ExportedPrompt[] = [{ name: 'Deleted', mode: SessionMode.BUILD }]
    const edited: ExportedPrompt[] = []

    const diff = calculateDiff(current, edited)

    expect(diff.creates).toHaveLength(0)
    expect(diff.updates).toHaveLength(0)
    expect(diff.deletes).toHaveLength(1)
    expect(diff.deletes[0]).toBe('Deleted')
  })

  it('does not include Issue Agent Prompts in deletes', () => {
    const current: ExportedPrompt[] = [
      {
        name: 'Issue Modify',
        mode: SessionMode.BUILD,
        sessionType: SessionType.ISSUE_AGENT_MODIFICATION,
      },
    ]
    const edited: ExportedPrompt[] = []

    const diff = calculateDiff(current, edited)

    // Issue Agent Prompts should not be deleted
    expect(diff.deletes).toHaveLength(0)
  })

  it('handles complex scenario with creates, updates, and deletes', () => {
    const current: ExportedPrompt[] = [
      { name: 'Stays', mode: SessionMode.BUILD },
      { name: 'Will Update', mode: SessionMode.BUILD },
      { name: 'Will Delete', mode: SessionMode.PLAN },
    ]
    const edited: ExportedPrompt[] = [
      { name: 'Stays', mode: SessionMode.BUILD },
      { name: 'Will Update', mode: SessionMode.PLAN },
      { name: 'Brand New', mode: SessionMode.BUILD },
    ]

    const diff = calculateDiff(current, edited)

    expect(diff.creates).toHaveLength(1)
    expect(diff.creates[0].name).toBe('Brand New')

    expect(diff.updates).toHaveLength(1)
    expect(diff.updates[0]).toEqual({
      name: 'Will Update',
      mode: SessionMode.PLAN,
    })

    expect(diff.deletes).toHaveLength(1)
    expect(diff.deletes[0]).toBe('Will Delete')
  })

  it('does not include unchanged prompts in updates', () => {
    const current: ExportedPrompt[] = [
      { name: 'Same', initialMessage: 'Hello', mode: SessionMode.BUILD },
    ]
    const edited: ExportedPrompt[] = [
      { name: 'Same', initialMessage: 'Hello', mode: SessionMode.BUILD },
    ]

    const diff = calculateDiff(current, edited)

    expect(diff.creates).toHaveLength(0)
    expect(diff.updates).toHaveLength(0)
    expect(diff.deletes).toHaveLength(0)
  })

  it('detects changes in initialMessage', () => {
    const current: ExportedPrompt[] = [
      { name: 'Same', initialMessage: 'Old', mode: SessionMode.BUILD },
    ]
    const edited: ExportedPrompt[] = [
      { name: 'Same', initialMessage: 'New', mode: SessionMode.BUILD },
    ]

    const diff = calculateDiff(current, edited)

    expect(diff.updates).toHaveLength(1)
    expect(diff.updates[0].initialMessage).toBe('New')
  })

  it('handles null vs undefined initialMessage', () => {
    const current: ExportedPrompt[] = [
      { name: 'Test', initialMessage: null, mode: SessionMode.BUILD },
    ]
    const edited: ExportedPrompt[] = [{ name: 'Test', mode: SessionMode.BUILD }]

    const diff = calculateDiff(current, edited)

    // Null and undefined should be treated as equivalent (no change)
    expect(diff.updates).toHaveLength(0)
  })

  it('allows updates to Issue Agent Prompts', () => {
    const current: ExportedPrompt[] = [
      {
        name: 'Issue Modify',
        initialMessage: 'Old Message',
        mode: SessionMode.BUILD,
        sessionType: SessionType.ISSUE_AGENT_MODIFICATION,
      },
    ]
    const edited: ExportedPrompt[] = [
      {
        name: 'Issue Modify',
        initialMessage: 'New Message',
        mode: SessionMode.BUILD,
        sessionType: SessionType.ISSUE_AGENT_MODIFICATION,
      },
    ]

    const diff = calculateDiff(current, edited)

    expect(diff.updates).toHaveLength(1)
    expect(diff.updates[0].initialMessage).toBe('New Message')
    expect(diff.deletes).toHaveLength(0)
  })
})
