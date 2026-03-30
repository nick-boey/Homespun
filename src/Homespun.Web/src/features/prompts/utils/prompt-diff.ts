import { SessionMode, SessionType } from '@/api/generated/types.gen'
import type { AgentPrompt } from '@/api/generated/types.gen'

/**
 * Exported prompt format - what we serialize to JSON for editing
 */
export type ExportedPrompt = {
  name: string | null
  initialMessage?: string | null
  mode: SessionMode
  sessionType?: SessionType
}

/**
 * Result of parsing prompts from JSON
 */
export type ParseResult =
  | { success: true; data: ExportedPrompt[] }
  | { success: false; error: string }

/**
 * Changes calculated between current and edited prompts
 */
export type PromptChanges = {
  creates: ExportedPrompt[]
  updates: (ExportedPrompt & { name: string })[]
  deletes: string[]
}

const VALID_SESSION_MODES = Object.values(SessionMode)
const VALID_SESSION_TYPES = Object.values(SessionType)

/**
 * Serialize prompts to formatted JSON for editing.
 * Excludes system fields like projectId, createdAt, updatedAt.
 */
export function serializePrompts(prompts: AgentPrompt[]): string {
  const exported: ExportedPrompt[] = prompts.map((prompt) => {
    const result: ExportedPrompt = {
      name: prompt.name ?? null,
      initialMessage: prompt.initialMessage,
      mode: prompt.mode ?? SessionMode.BUILD,
    }

    // Include sessionType for Issue Agent Prompts
    if (prompt.sessionType) {
      result.sessionType = prompt.sessionType
    }

    return result
  })

  return JSON.stringify(exported, null, 2)
}

/**
 * Parse and validate JSON string into prompts array.
 */
export function parsePrompts(json: string): ParseResult {
  let parsed: unknown
  try {
    parsed = JSON.parse(json)
  } catch {
    return { success: false, error: 'Invalid JSON: Could not parse the input' }
  }

  if (!Array.isArray(parsed)) {
    return { success: false, error: 'Prompts must be an array' }
  }

  const prompts: ExportedPrompt[] = []
  for (let i = 0; i < parsed.length; i++) {
    const item = parsed[i]

    if (typeof item !== 'object' || item === null) {
      return { success: false, error: `Item at index ${i} is not an object` }
    }

    const obj = item as Record<string, unknown>

    // Validate name
    if (obj.name === undefined || obj.name === null) {
      return { success: false, error: `Item at index ${i}: name is required` }
    }
    if (typeof obj.name !== 'string') {
      return { success: false, error: `Item at index ${i}: name must be a string` }
    }

    // Validate mode
    if (obj.mode === undefined) {
      return { success: false, error: `Item at index ${i}: mode is required` }
    }
    if (!VALID_SESSION_MODES.includes(obj.mode as SessionMode)) {
      return {
        success: false,
        error: `Item at index ${i}: mode must be one of: ${VALID_SESSION_MODES.join(', ')}`,
      }
    }

    // Validate sessionType if present
    if (obj.sessionType !== undefined && obj.sessionType !== null) {
      if (!VALID_SESSION_TYPES.includes(obj.sessionType as SessionType)) {
        return {
          success: false,
          error: `Item at index ${i}: sessionType must be one of: ${VALID_SESSION_TYPES.join(', ')}`,
        }
      }
    }

    // Build validated prompt
    const prompt: ExportedPrompt = {
      name: obj.name,
      mode: obj.mode as SessionMode,
    }

    if (obj.initialMessage !== undefined) {
      prompt.initialMessage = obj.initialMessage as string | null
    }

    if (obj.sessionType !== undefined && obj.sessionType !== null) {
      prompt.sessionType = obj.sessionType as SessionType
    }

    prompts.push(prompt)
  }

  return { success: true, data: prompts }
}

/**
 * Check if a prompt is an Issue Agent Prompt (which cannot be deleted).
 */
function isIssueAgentPrompt(prompt: ExportedPrompt): boolean {
  return (
    prompt.sessionType === SessionType.ISSUE_AGENT_MODIFICATION ||
    prompt.sessionType === SessionType.ISSUE_AGENT_SYSTEM
  )
}

/**
 * Normalize a value for comparison (treat null and undefined as equivalent).
 */
function normalizeValue(value: unknown): unknown {
  return value === null ? undefined : value
}

/**
 * Check if two prompts are equal (for detecting changes).
 */
function promptsEqual(a: ExportedPrompt, b: ExportedPrompt): boolean {
  return (
    a.name === b.name &&
    normalizeValue(a.initialMessage) === normalizeValue(b.initialMessage) &&
    a.mode === b.mode
  )
}

/**
 * Calculate the diff between current prompts and edited prompts.
 * Determines which prompts need to be created, updated, or deleted.
 *
 * Note: Issue Agent Prompts (with sessionType) cannot be deleted.
 */
export function calculateDiff(current: ExportedPrompt[], edited: ExportedPrompt[]): PromptChanges {
  const creates: ExportedPrompt[] = []
  const updates: (ExportedPrompt & { name: string })[] = []
  const deletes: string[] = []

  // Build a map of current prompts by name
  const currentByName = new Map<string, ExportedPrompt>()
  for (const prompt of current) {
    if (prompt.name) {
      currentByName.set(prompt.name, prompt)
    }
  }

  // Build a set of edited prompt names
  const editedNames = new Set<string>()
  for (const prompt of edited) {
    if (prompt.name) {
      editedNames.add(prompt.name)
    }
  }

  // Process edited prompts
  for (const prompt of edited) {
    if (!prompt.name) {
      continue
    }
    const currentPrompt = currentByName.get(prompt.name)
    if (!currentPrompt) {
      // New prompt (name not in current)
      creates.push(prompt)
    } else if (!promptsEqual(currentPrompt, prompt)) {
      // Existing prompt - changed
      updates.push({
        name: prompt.name,
        initialMessage: prompt.initialMessage,
        mode: prompt.mode,
        sessionType: prompt.sessionType,
      })
    }
  }

  // Find deleted prompts (in current but not in edited)
  for (const prompt of current) {
    if (prompt.name && !editedNames.has(prompt.name)) {
      // Skip Issue Agent Prompts - they cannot be deleted
      if (!isIssueAgentPrompt(prompt)) {
        deletes.push(prompt.name)
      }
    }
  }

  return { creates, updates, deletes }
}
