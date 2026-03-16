import { describe, it, expect } from 'vitest'
import { isAgentConflictError, type AgentConflictError } from './use-run-agent'
import { ClaudeSessionStatus } from '@/api'

// Helper to create a mock AgentConflictError for testing
function createMockAgentConflictError(
  sessionId: string,
  status: ClaudeSessionStatus
): AgentConflictError {
  const error = new Error('An agent is already running on this issue') as AgentConflictError
  error.name = 'AgentConflictError'
  error.sessionId = sessionId
  error.status = status
  return error
}

describe('isAgentConflictError', () => {
  it('returns true for AgentConflictError', () => {
    const error = createMockAgentConflictError('session-123', ClaudeSessionStatus.RUNNING)

    expect(isAgentConflictError(error)).toBe(true)
  })

  it('detects sessionId and status properties', () => {
    const error = createMockAgentConflictError('session-123', ClaudeSessionStatus.RUNNING)

    expect(error.sessionId).toBe('session-123')
    expect(error.status).toBe(ClaudeSessionStatus.RUNNING)
    expect(error.name).toBe('AgentConflictError')
  })

  it('returns false for regular Error', () => {
    const error = new Error('Some error')

    expect(isAgentConflictError(error)).toBe(false)
  })

  it('returns false for null', () => {
    expect(isAgentConflictError(null)).toBe(false)
  })

  it('returns false for undefined', () => {
    expect(isAgentConflictError(undefined)).toBe(false)
  })

  it('returns false for string', () => {
    expect(isAgentConflictError('error')).toBe(false)
  })

  it('returns false for plain object without name', () => {
    expect(isAgentConflictError({ sessionId: '123', status: 'running' })).toBe(false)
  })

  it('returns false for object with wrong name', () => {
    const obj = { name: 'OtherError', sessionId: '123', status: 'running' }
    expect(isAgentConflictError(obj)).toBe(false)
  })

  it('returns true for object matching AgentConflictError shape', () => {
    const obj = { name: 'AgentConflictError', sessionId: '123', status: 'running' }
    expect(isAgentConflictError(obj)).toBe(true)
  })
})
