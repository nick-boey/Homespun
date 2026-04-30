import { describe, it, expect } from 'vitest'
import { ClaudeSessionStatus, SessionMode } from '@/api'
import type { SessionSummary } from '@/api/generated/types.gen'
import { groupSessionsByProject } from './group-sessions-by-project'

function s(overrides: Partial<SessionSummary> = {}): SessionSummary {
  return {
    id: overrides.id ?? 'session-default',
    entityId: 'entity-1',
    projectId: 'project-1',
    model: 'sonnet',
    mode: SessionMode.BUILD,
    status: ClaudeSessionStatus.RUNNING,
    createdAt: '2024-01-01T10:00:00Z',
    lastActivityAt: '2024-01-01T10:00:00Z',
    ...overrides,
  }
}

describe('groupSessionsByProject', () => {
  it('filters out STOPPED sessions', () => {
    const sessions = [
      s({ id: 'a', status: ClaudeSessionStatus.RUNNING }),
      s({ id: 'b', status: ClaudeSessionStatus.STOPPED }),
      s({ id: 'c', status: ClaudeSessionStatus.WAITING_FOR_INPUT }),
    ]

    const grouped = groupSessionsByProject(sessions)

    const ids = grouped.get('project-1')!.map((session) => session.id)
    expect(ids).toEqual(['a', 'c'])
    expect(ids).not.toContain('b')
  })

  it('includes ERROR sessions', () => {
    const sessions = [s({ id: 'err', status: ClaudeSessionStatus.ERROR, projectId: 'project-x' })]

    const grouped = groupSessionsByProject(sessions)

    expect(grouped.get('project-x')!.map((x) => x.id)).toEqual(['err'])
  })

  it('sorts each group by createdAt ascending', () => {
    const sessions = [
      s({ id: 'newest', createdAt: '2024-01-01T11:00:00Z' }),
      s({ id: 'oldest', createdAt: '2024-01-01T09:00:00Z' }),
      s({ id: 'middle', createdAt: '2024-01-01T10:00:00Z' }),
    ]

    const grouped = groupSessionsByProject(sessions)
    const ids = grouped.get('project-1')!.map((x) => x.id)

    expect(ids).toEqual(['oldest', 'middle', 'newest'])
  })

  it('groups under each project independently', () => {
    const sessions = [
      s({ id: 'a1', projectId: 'A', createdAt: '2024-01-01T09:00:00Z' }),
      s({ id: 'a2', projectId: 'A', createdAt: '2024-01-01T10:00:00Z' }),
      s({ id: 'b1', projectId: 'B', createdAt: '2024-01-01T08:00:00Z' }),
    ]

    const grouped = groupSessionsByProject(sessions)

    expect(grouped.get('A')!.map((x) => x.id)).toEqual(['a1', 'a2'])
    expect(grouped.get('B')!.map((x) => x.id)).toEqual(['b1'])
  })

  it('excludes sessions whose projectId is not in knownProjectIds', () => {
    const sessions = [
      s({ id: 'known', projectId: 'known-project' }),
      s({ id: 'orphan', projectId: 'mystery-project' }),
    ]

    const grouped = groupSessionsByProject(sessions, new Set(['known-project']))

    expect(grouped.has('known-project')).toBe(true)
    expect(grouped.get('known-project')!.map((x) => x.id)).toEqual(['known'])
    expect(grouped.has('mystery-project')).toBe(false)
  })

  it('excludes sessions with null/undefined projectId', () => {
    const sessions = [
      s({ id: 'no-proj', projectId: null }),
      s({ id: 'has-proj', projectId: 'project-1' }),
    ]

    const grouped = groupSessionsByProject(sessions)

    expect(grouped.get('project-1')!.map((x) => x.id)).toEqual(['has-proj'])
    expect(grouped.size).toBe(1)
  })

  it('returns empty map when sessions is undefined', () => {
    expect(groupSessionsByProject(undefined).size).toBe(0)
  })
})
