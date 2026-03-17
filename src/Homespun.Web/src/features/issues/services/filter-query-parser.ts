/**
 * Filter Query Parser for Fleece Issue Filtering
 *
 * Supports the Fleece query language syntax:
 * - Field filters: status:open, type:bug, priority:1, assigned:john, tag:frontend
 * - Negation: -status:open, -type:bug
 * - Multi-value: type:bug,feature; (semicolon-terminated, OR logic)
 * - Keyed tags: project:frontend (matches project=frontend)
 * - Free text: unrecognized words search title/description/tags/id
 * - Combination: status:open type:bug login (AND logic between filters)
 */

import type { IssueResponse } from '@/api'
import { IssueStatus, IssueType } from '@/api/generated'

export interface ParsedFilter {
  status?: IssueStatus[]
  statusNegated?: IssueStatus[]
  type?: IssueType[]
  typeNegated?: IssueType[]
  priority?: number[]
  assignee?: string[]
  assigneeMe?: boolean
  resolvedMeEmail?: string
  tags?: string[]
  linkedPr?: number[]
  id?: string[]
  isNext?: boolean
  freeText: string[]
  errors: string[]
}

// Status value mapping (including aliases)
const STATUS_MAP: Record<string, IssueStatus> = {
  draft: IssueStatus.DRAFT,
  open: IssueStatus.OPEN,
  progress: IssueStatus.PROGRESS,
  inprogress: IssueStatus.PROGRESS, // alias
  review: IssueStatus.REVIEW,
  complete: IssueStatus.COMPLETE,
  done: IssueStatus.COMPLETE, // alias
  archived: IssueStatus.ARCHIVED,
  closed: IssueStatus.CLOSED,
  deleted: IssueStatus.DELETED,
}

// Type value mapping (including aliases)
const TYPE_MAP: Record<string, IssueType> = {
  task: IssueType.TASK,
  bug: IssueType.BUG,
  fix: IssueType.BUG, // alias
  chore: IssueType.CHORE,
  feature: IssueType.FEATURE,
  feat: IssueType.FEATURE, // alias
  idea: IssueType.IDEA,
  verify: IssueType.VERIFY,
}

// Known filter field names (keys that should be interpreted as keyed tags)
const KNOWN_FIELDS = new Set([
  'status',
  'type',
  'priority',
  'p',
  'assigned',
  'assignee',
  'tag',
  'tags',
  'pr',
  'id',
  'is',
])

/**
 * Parses a filter query string into a structured ParsedFilter object.
 */
export function parseFilterQuery(query: string): ParsedFilter {
  const result: ParsedFilter = {
    freeText: [],
    errors: [],
  }

  const trimmed = query.trim()
  if (!trimmed) {
    return result
  }

  // Split by whitespace, but handle multi-value syntax
  const tokens = tokenize(trimmed)

  for (const token of tokens) {
    parseToken(token, result)
  }

  return result
}

/**
 * Tokenizes a query string into individual tokens.
 * Handles multi-value syntax with comma-semicolon.
 */
function tokenize(query: string): string[] {
  const tokens: string[] = []
  let current = ''
  let i = 0

  while (i < query.length) {
    const char = query[i]

    if (char === ' ' || char === '\t') {
      if (current) {
        tokens.push(current)
        current = ''
      }
      i++
    } else if (char === ';') {
      // Semicolon terminates multi-value, include it in current token
      current += char
      if (current) {
        tokens.push(current)
        current = ''
      }
      i++
    } else {
      current += char
      i++
    }
  }

  if (current) {
    tokens.push(current)
  }

  return tokens
}

/**
 * Parses a single token and updates the result.
 */
function parseToken(token: string, result: ParsedFilter): void {
  // Check for negation
  const isNegated = token.startsWith('-')
  const cleanToken = isNegated ? token.slice(1) : token

  // Check for field:value pattern
  const colonIndex = cleanToken.indexOf(':')
  if (colonIndex === -1) {
    // No colon - treat as free text
    result.freeText.push(token)
    return
  }

  const field = cleanToken.slice(0, colonIndex).toLowerCase()
  let value = cleanToken.slice(colonIndex + 1)

  // Remove trailing semicolon if present
  if (value.endsWith(';')) {
    value = value.slice(0, -1)
  }

  // Parse based on field type
  switch (field) {
    case 'status':
      parseStatusFilter(value, isNegated, result)
      break
    case 'type':
      parseTypeFilter(value, isNegated, result)
      break
    case 'priority':
    case 'p':
      parsePriorityFilter(value, result)
      break
    case 'assigned':
    case 'assignee':
      parseAssigneeFilter(value, result)
      break
    case 'tag':
    case 'tags':
      parseTagFilter(value, result)
      break
    case 'pr':
      parsePrFilter(value, result)
      break
    case 'id':
      parseIdFilter(value, result)
      break
    case 'is':
      parseIsFilter(value, result)
      break
    default:
      // Unknown field - treat as keyed tag (project:value => project=value)
      if (!KNOWN_FIELDS.has(field)) {
        if (!result.tags) result.tags = []
        result.tags.push(`${field}=${value}`)
      } else {
        // This shouldn't happen, but handle it
        result.freeText.push(token)
      }
      break
  }
}

/**
 * Parses status filter values.
 */
function parseStatusFilter(value: string, isNegated: boolean, result: ParsedFilter): void {
  const values = value.split(',').map((v) => v.trim().toLowerCase())
  const parsed: IssueStatus[] = []

  for (const v of values) {
    if (!v) continue
    const statusValue = STATUS_MAP[v]
    if (statusValue === undefined) {
      result.errors.push(`Unknown status value: '${v}'`)
    } else {
      parsed.push(statusValue)
    }
  }

  if (parsed.length > 0) {
    if (isNegated) {
      result.statusNegated = [...(result.statusNegated ?? []), ...parsed]
    } else {
      result.status = [...(result.status ?? []), ...parsed]
    }
  }
}

/**
 * Parses type filter values.
 */
function parseTypeFilter(value: string, isNegated: boolean, result: ParsedFilter): void {
  const values = value.split(',').map((v) => v.trim().toLowerCase())
  const parsed: IssueType[] = []

  for (const v of values) {
    if (!v) continue
    const typeValue = TYPE_MAP[v]
    if (typeValue === undefined) {
      result.errors.push(`Unknown type value: '${v}'`)
    } else {
      parsed.push(typeValue)
    }
  }

  if (parsed.length > 0) {
    if (isNegated) {
      result.typeNegated = [...(result.typeNegated ?? []), ...parsed]
    } else {
      result.type = [...(result.type ?? []), ...parsed]
    }
  }
}

/**
 * Parses priority filter values.
 */
function parsePriorityFilter(value: string, result: ParsedFilter): void {
  const values = value.split(',').map((v) => v.trim())

  for (const v of values) {
    if (!v) continue
    const num = parseInt(v, 10)
    if (isNaN(num)) {
      result.errors.push(`Invalid priority value: '${v}'`)
    } else {
      if (!result.priority) result.priority = []
      result.priority.push(num)
    }
  }
}

/**
 * Parses assignee filter values.
 * Handles the special "me" keyword to filter by current user.
 */
function parseAssigneeFilter(value: string, result: ParsedFilter): void {
  const values = value.split(',').map((v) => v.trim())

  for (const v of values) {
    if (!v) continue
    if (v.toLowerCase() === 'me') {
      result.assigneeMe = true
    } else {
      if (!result.assignee) result.assignee = []
      result.assignee.push(v)
    }
  }
}

/**
 * Parses tag filter values.
 */
function parseTagFilter(value: string, result: ParsedFilter): void {
  const values = value.split(',').map((v) => v.trim())

  for (const v of values) {
    if (!v) continue
    if (!result.tags) result.tags = []
    result.tags.push(v)
  }
}

/**
 * Parses PR filter values.
 */
function parsePrFilter(value: string, result: ParsedFilter): void {
  const values = value.split(',').map((v) => v.trim())

  for (const v of values) {
    if (!v) continue
    const num = parseInt(v, 10)
    if (!isNaN(num)) {
      if (!result.linkedPr) result.linkedPr = []
      result.linkedPr.push(num)
    }
  }
}

/**
 * Parses ID filter values.
 */
function parseIdFilter(value: string, result: ParsedFilter): void {
  const values = value.split(',').map((v) => v.trim())

  for (const v of values) {
    if (!v) continue
    if (!result.id) result.id = []
    result.id.push(v)
  }
}

/**
 * Parses "is" filter values (e.g., is:next, is:actionable).
 */
function parseIsFilter(value: string, result: ParsedFilter): void {
  const v = value.trim().toLowerCase()

  switch (v) {
    case 'next':
    case 'actionable':
      result.isNext = true
      break
    default:
      result.errors.push(`Unknown 'is' filter value: '${value}'`)
      break
  }
}

/**
 * Applies a parsed filter to an issue and returns whether it matches.
 */
export function applyFilter(issue: IssueResponse, filter: ParsedFilter): boolean {
  // Status filter
  if (filter.status && filter.status.length > 0) {
    if (issue.status === undefined || !filter.status.includes(issue.status)) {
      return false
    }
  }

  // Negated status filter
  if (filter.statusNegated && filter.statusNegated.length > 0) {
    if (issue.status !== undefined && filter.statusNegated.includes(issue.status)) {
      return false
    }
  }

  // Type filter
  if (filter.type && filter.type.length > 0) {
    if (issue.type === undefined || !filter.type.includes(issue.type)) {
      return false
    }
  }

  // Negated type filter
  if (filter.typeNegated && filter.typeNegated.length > 0) {
    if (issue.type !== undefined && filter.typeNegated.includes(issue.type)) {
      return false
    }
  }

  // Priority filter
  if (filter.priority && filter.priority.length > 0) {
    if (issue.priority === undefined || issue.priority === null) {
      return false
    }
    if (!filter.priority.includes(issue.priority)) {
      return false
    }
  }

  // Assignee filter
  if (filter.assignee && filter.assignee.length > 0) {
    if (!issue.assignedTo) {
      return false
    }
    const assignedLower = issue.assignedTo.toLowerCase()
    const matches = filter.assignee.some((a) => assignedLower.includes(a.toLowerCase()))
    if (!matches) {
      return false
    }
  }

  // Assignee:me filter with resolved email
  if (filter.assigneeMe && filter.resolvedMeEmail) {
    if (!issue.assignedTo) {
      return false
    }
    if (issue.assignedTo.toLowerCase() !== filter.resolvedMeEmail.toLowerCase()) {
      return false
    }
  }

  // Tag filter
  if (filter.tags && filter.tags.length > 0) {
    if (!issue.tags || issue.tags.length === 0) {
      return false
    }
    const issueTags = issue.tags.map((t) => t.toLowerCase())
    const matches = filter.tags.every((filterTag) =>
      issueTags.some((issueTag) => issueTag.includes(filterTag.toLowerCase()))
    )
    if (!matches) {
      return false
    }
  }

  // Linked PR filter
  if (filter.linkedPr && filter.linkedPr.length > 0) {
    if (!issue.linkedPRs || issue.linkedPRs.length === 0) {
      return false
    }
    const matches = filter.linkedPr.some((pr) => issue.linkedPRs?.includes(pr))
    if (!matches) {
      return false
    }
  }

  // ID filter
  if (filter.id && filter.id.length > 0) {
    if (!issue.id) {
      return false
    }
    const idLower = issue.id.toLowerCase()
    const matches = filter.id.some((filterId) => idLower.startsWith(filterId.toLowerCase()))
    if (!matches) {
      return false
    }
  }

  // Free text filter (AND logic - all words must match somewhere)
  if (filter.freeText.length > 0) {
    const searchableText = [
      issue.id ?? '',
      issue.title ?? '',
      issue.description ?? '',
      ...(issue.tags ?? []),
    ]
      .join(' ')
      .toLowerCase()

    for (const word of filter.freeText) {
      if (!searchableText.includes(word.toLowerCase())) {
        return false
      }
    }
  }

  return true
}
