import { describe, it, expect } from 'vitest'
import { computeInheritedParentInfo } from './inherited-parent'
import type { TaskGraphResponse, TaskGraphNodeResponse, IssueResponse } from '@/api'
import { IssueStatus, IssueType, ExecutionMode } from '@/api'

// Helper to create a mock issue
function createIssue(id: string, parentIssueId?: string, sortOrder?: string): IssueResponse {
  return {
    id,
    title: `Issue ${id}`,
    description: null,
    status: IssueStatus.OPEN,
    type: IssueType.TASK,
    priority: null,
    linkedPRs: [],
    linkedIssues: null,
    parentIssues: parentIssueId
      ? [{ parentIssue: parentIssueId, sortOrder: sortOrder ?? 'V' }]
      : [],
    tags: null,
    workingBranchId: null,
    executionMode: ExecutionMode.SERIES,
    createdBy: null,
    assignedTo: null,
    lastUpdate: undefined,
    createdAt: undefined,
  }
}

// Helper to create a mock node
function createNode(issue: IssueResponse, row: number): TaskGraphNodeResponse {
  return {
    issue,
    lane: 0,
    row,
    isActionable: false,
  }
}

describe('computeInheritedParentInfo', () => {
  describe('when taskGraph is null or undefined', () => {
    it('returns null when taskGraph is null', () => {
      const result = computeInheritedParentInfo(null, 'issue-1', false)
      expect(result).toBeNull()
    })

    it('returns null when taskGraph is undefined', () => {
      const result = computeInheritedParentInfo(undefined, 'issue-1', false)
      expect(result).toBeNull()
    })
  })

  describe('when referenceIssueId is missing', () => {
    it('returns null when referenceIssueId is undefined', () => {
      const taskGraph: TaskGraphResponse = { nodes: [] }
      const result = computeInheritedParentInfo(taskGraph, undefined, false)
      expect(result).toBeNull()
    })
  })

  describe('when reference issue is not found', () => {
    it('returns null when reference issue does not exist in graph', () => {
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(createIssue('other-issue'), 0)],
      }
      const result = computeInheritedParentInfo(taskGraph, 'non-existent', false)
      expect(result).toBeNull()
    })
  })

  describe('when reference issue has no parent (orphan)', () => {
    it('returns null parentIssueId for orphan reference issue', () => {
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(createIssue('orphan-issue'), 0)],
      }
      const result = computeInheritedParentInfo(taskGraph, 'orphan-issue', false)
      expect(result).toEqual({ parentIssueId: null, sortOrder: null })
    })
  })

  describe('when reference issue has a parent', () => {
    it('inherits the parent from reference issue', () => {
      const taskGraph: TaskGraphResponse = {
        nodes: [
          createNode(createIssue('child-1', 'parent-1', 'a'), 0),
          createNode(createIssue('parent-1'), 1),
        ],
      }
      const result = computeInheritedParentInfo(taskGraph, 'child-1', false)
      expect(result?.parentIssueId).toBe('parent-1')
      expect(result?.sortOrder).toBeDefined()
    })
  })

  describe('sort order computation', () => {
    describe('creating below reference (isAbove=false)', () => {
      it('computes sort order after the reference sibling', () => {
        const taskGraph: TaskGraphResponse = {
          nodes: [
            createNode(createIssue('child-1', 'parent-1', 'a'), 0),
            createNode(createIssue('child-2', 'parent-1', 'b'), 1),
            createNode(createIssue('parent-1'), 2),
          ],
        }
        // Creating below child-1, sort should be between 'a' and 'b'
        const result = computeInheritedParentInfo(taskGraph, 'child-1', false)
        expect(result?.parentIssueId).toBe('parent-1')
        expect(result?.sortOrder).toBeDefined()
        expect(result!.sortOrder! > 'a').toBe(true)
        expect(result!.sortOrder! < 'b').toBe(true)
      })

      it('computes sort order after last sibling when reference is last', () => {
        const taskGraph: TaskGraphResponse = {
          nodes: [
            createNode(createIssue('child-1', 'parent-1', 'a'), 0),
            createNode(createIssue('child-2', 'parent-1', 'b'), 1),
            createNode(createIssue('parent-1'), 2),
          ],
        }
        // Creating below child-2 (last sibling)
        const result = computeInheritedParentInfo(taskGraph, 'child-2', false)
        expect(result?.parentIssueId).toBe('parent-1')
        expect(result!.sortOrder! > 'b').toBe(true) // After 'b'
      })
    })

    describe('creating above reference (isAbove=true)', () => {
      it('computes sort order before the reference sibling', () => {
        const taskGraph: TaskGraphResponse = {
          nodes: [
            createNode(createIssue('child-1', 'parent-1', 'a'), 0),
            createNode(createIssue('child-2', 'parent-1', 'b'), 1),
            createNode(createIssue('parent-1'), 2),
          ],
        }
        // Creating above child-2, sort should be between 'a' and 'b'
        const result = computeInheritedParentInfo(taskGraph, 'child-2', true)
        expect(result?.parentIssueId).toBe('parent-1')
        expect(result!.sortOrder! > 'a').toBe(true)
        expect(result!.sortOrder! < 'b').toBe(true)
      })

      it('computes sort order before first sibling when reference is first', () => {
        const taskGraph: TaskGraphResponse = {
          nodes: [
            createNode(createIssue('child-1', 'parent-1', 'V'), 0),
            createNode(createIssue('child-2', 'parent-1', 'b'), 1),
            createNode(createIssue('parent-1'), 2),
          ],
        }
        // Creating above child-1 (first sibling)
        const result = computeInheritedParentInfo(taskGraph, 'child-1', true)
        expect(result?.parentIssueId).toBe('parent-1')
        expect(result!.sortOrder! < 'V').toBe(true) // Before 'V'
      })
    })
  })

  describe('case insensitivity', () => {
    it('finds reference issue regardless of case', () => {
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(createIssue('AbCdEf', 'parent-1', 'a'), 0)],
      }
      const result = computeInheritedParentInfo(taskGraph, 'abcdef', false)
      expect(result?.parentIssueId).toBe('parent-1')
    })
  })
})
