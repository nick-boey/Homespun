import { describe, it, expect } from 'vitest'
import {
  KeyboardEditMode,
  MoveOperationType,
  EditCursorPosition,
  MoveDirection,
  TYPE_CYCLE_ORDER,
  getNextIssueType,
  TYPE_CYCLE_DEBOUNCE_MS,
} from './types'

describe('KeyboardEditMode enum', () => {
  it('has all required modes', () => {
    expect(KeyboardEditMode.Viewing).toBe('Viewing')
    expect(KeyboardEditMode.EditingExisting).toBe('EditingExisting')
    expect(KeyboardEditMode.CreatingNew).toBe('CreatingNew')
    expect(KeyboardEditMode.SelectingAgentPrompt).toBe('SelectingAgentPrompt')
    expect(KeyboardEditMode.SelectingMoveTarget).toBe('SelectingMoveTarget')
  })
})

describe('MoveOperationType enum', () => {
  it('has all required types', () => {
    expect(MoveOperationType.AsChildOf).toBe('AsChildOf')
    expect(MoveOperationType.AsParentOf).toBe('AsParentOf')
  })
})

describe('EditCursorPosition enum', () => {
  it('has all required positions', () => {
    expect(EditCursorPosition.Start).toBe('Start')
    expect(EditCursorPosition.End).toBe('End')
    expect(EditCursorPosition.Replace).toBe('Replace')
  })
})

describe('MoveDirection enum', () => {
  it('has all required directions', () => {
    expect(MoveDirection.Up).toBe('Up')
    expect(MoveDirection.Down).toBe('Down')
  })
})

describe('TYPE_CYCLE_ORDER', () => {
  it('has correct order: Task(0) -> Bug(1) -> Feature(2) -> Chore(3)', () => {
    expect(TYPE_CYCLE_ORDER).toEqual([0, 1, 2, 3])
  })
})

describe('getNextIssueType', () => {
  it('cycles Task(0) -> Bug(1)', () => {
    expect(getNextIssueType(0)).toBe(1)
  })

  it('cycles Bug(1) -> Feature(2)', () => {
    expect(getNextIssueType(1)).toBe(2)
  })

  it('cycles Feature(2) -> Chore(3)', () => {
    expect(getNextIssueType(2)).toBe(3)
  })

  it('cycles Chore(3) -> Task(0)', () => {
    expect(getNextIssueType(3)).toBe(0)
  })

  it('defaults to Task(0) for unknown types', () => {
    expect(getNextIssueType(99)).toBe(0)
  })
})

describe('TYPE_CYCLE_DEBOUNCE_MS', () => {
  it('is 3 seconds (3000ms)', () => {
    expect(TYPE_CYCLE_DEBOUNCE_MS).toBe(3000)
  })
})
