import { describe, expect, it } from 'vitest'
import type { TaskGraphResponse } from '@/api'
import type { IssueFieldPatch } from '@/types/signalr'
import { applyPatch } from './apply-patch'

function buildResponse(overrides: Partial<TaskGraphResponse> = {}): TaskGraphResponse {
  return {
    nodes: [
      {
        lane: 0,
        row: 0,
        issue: {
          id: 'A',
          title: 'Alpha',
          description: 'first',
          priority: 1,
          tags: ['one'],
          status: 'open',
          type: 'task',
        },
      },
      {
        lane: 0,
        row: 1,
        issue: {
          id: 'B',
          title: 'Bravo',
          description: 'second',
          priority: 2,
          tags: [],
          status: 'open',
          type: 'task',
        },
      },
    ],
    totalLanes: 1,
    ...overrides,
  }
}

describe('applyPatch', () => {
  it('updates the matching node when the issue is present', () => {
    const response = buildResponse()
    const patch: IssueFieldPatch = { title: 'Alpha v2' }

    const next = applyPatch(response, 'A', patch)

    expect(next?.nodes?.[0].issue?.title).toBe('Alpha v2')
  })

  it('returns the input reference unchanged when the issue is missing', () => {
    const response = buildResponse()

    const next = applyPatch(response, 'does-not-exist', { title: 'nope' })

    expect(next).toBe(response)
  })

  it('returns undefined unchanged when the input is undefined', () => {
    const next = applyPatch(undefined, 'A', { title: 'whatever' })

    expect(next).toBeUndefined()
  })

  it('preserves fields that were not in the patch', () => {
    const response = buildResponse()
    const patch: IssueFieldPatch = { title: 'New Title' }

    const next = applyPatch(response, 'A', patch)
    const patchedIssue = next?.nodes?.[0].issue

    expect(patchedIssue?.description).toBe('first')
    expect(patchedIssue?.priority).toBe(1)
    expect(patchedIssue?.tags).toEqual(['one'])
    expect(patchedIssue?.status).toBe('open')
  })

  it('preserves other nodes untouched', () => {
    const response = buildResponse()
    const originalOther = response.nodes![1]

    const next = applyPatch(response, 'A', { title: 'New Title' })

    expect(next?.nodes?.[1]).toBe(originalOther)
  })

  it('does not mutate the input response or its nodes', () => {
    const response = buildResponse()
    const originalFirstNode = response.nodes![0]
    const originalFirstIssue = response.nodes![0].issue
    const originalNodes = response.nodes

    applyPatch(response, 'A', { title: 'Mutated?' })

    expect(response.nodes).toBe(originalNodes)
    expect(response.nodes![0]).toBe(originalFirstNode)
    expect(response.nodes![0].issue).toBe(originalFirstIssue)
    expect(response.nodes![0].issue?.title).toBe('Alpha')
  })

  it('returns a new response object when patch hits a node', () => {
    const response = buildResponse()

    const next = applyPatch(response, 'A', { title: 'New Title' })

    expect(next).not.toBe(response)
    expect(next?.nodes).not.toBe(response.nodes)
    expect(next?.nodes?.[0]).not.toBe(response.nodes?.[0])
  })

  it('ignores null and undefined patch fields', () => {
    const response = buildResponse()
    const patch: IssueFieldPatch = {
      title: 'Kept',
      description: null,
      priority: undefined,
    }

    const next = applyPatch(response, 'A', patch)
    const patchedIssue = next?.nodes?.[0].issue

    expect(patchedIssue?.title).toBe('Kept')
    expect(patchedIssue?.description).toBe('first')
    expect(patchedIssue?.priority).toBe(1)
  })

  it('applies multi-field patches in one pass', () => {
    const response = buildResponse()
    const patch: IssueFieldPatch = {
      title: 'NewTitle',
      description: 'NewDesc',
      priority: 9,
      tags: ['fresh', 'tags'],
      assignedTo: 'alice',
      lastUpdate: '2026-04-21T00:00:00Z',
    }

    const next = applyPatch(response, 'A', patch)
    const patchedIssue = next?.nodes?.[0].issue

    expect(patchedIssue?.title).toBe('NewTitle')
    expect(patchedIssue?.description).toBe('NewDesc')
    expect(patchedIssue?.priority).toBe(9)
    expect(patchedIssue?.tags).toEqual(['fresh', 'tags'])
    expect(patchedIssue?.assignedTo).toBe('alice')
    expect(patchedIssue?.lastUpdate).toBe('2026-04-21T00:00:00Z')
  })

  it('returns the input reference unchanged when nodes is empty', () => {
    const response: TaskGraphResponse = { nodes: [], totalLanes: 0 }

    const next = applyPatch(response, 'A', { title: 'nope' })

    expect(next).toBe(response)
  })
})
