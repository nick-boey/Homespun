import { useMemo } from 'react'

export type TriggerType = '@' | '#'

export interface MentionTriggerState {
  /** Whether a mention trigger is currently active */
  active: boolean
  /** The type of trigger (@ for files, # for PRs) */
  type: TriggerType | null
  /** The search query after the trigger character */
  query: string
  /** Position of the trigger character in the input */
  triggerPosition: number
}

const INACTIVE_STATE: MentionTriggerState = {
  active: false,
  type: null,
  query: '',
  triggerPosition: -1,
}

/**
 * Detects if the cursor is positioned after a mention trigger (@ or #).
 * Returns the trigger type and query for use in search functionality.
 *
 * @param value - The current input value
 * @param cursorPosition - The current cursor position (selectionStart)
 * @returns MentionTriggerState with active flag, trigger type, query, and position
 */
export function detectMentionTrigger(value: string, cursorPosition: number): MentionTriggerState {
  if (!value || cursorPosition <= 0) {
    return INACTIVE_STATE
  }

  // Get text before cursor
  const textBeforeCursor = value.slice(0, cursorPosition)

  // Find the last @ or # that could be a trigger
  // A trigger is valid if it's at the start or preceded by whitespace
  let lastTriggerIndex = -1
  let lastTriggerType: TriggerType | null = null

  for (let i = textBeforeCursor.length - 1; i >= 0; i--) {
    const char = textBeforeCursor[i]

    // If we hit whitespace, check for trigger at next position or stop
    if (/\s/.test(char)) {
      // Check if there was a trigger after this whitespace
      if (lastTriggerIndex > i) {
        break // We found a valid trigger
      }
      continue
    }

    // Check for trigger characters
    if (char === '@' || char === '#') {
      // Trigger is valid if at start or preceded by whitespace
      const isAtStart = i === 0
      const isPrecededByWhitespace = i > 0 && /\s/.test(textBeforeCursor[i - 1])

      if (isAtStart || isPrecededByWhitespace) {
        lastTriggerIndex = i
        lastTriggerType = char
        break
      }
    }
  }

  if (lastTriggerIndex === -1 || lastTriggerType === null) {
    return INACTIVE_STATE
  }

  // Extract the query (text after trigger up to cursor)
  const query = textBeforeCursor.slice(lastTriggerIndex + 1)

  // Check if this is a completed reference based on trigger type
  if (lastTriggerType === '@') {
    // For @ triggers:
    // - If query starts with ", check if there's a closing " (quoted path complete)
    // - Otherwise, if query contains any whitespace, it's complete
    if (query.startsWith('"')) {
      // Quoted path - complete when closing quote is found
      const closingQuoteIndex = query.indexOf('"', 1)
      if (closingQuoteIndex !== -1) {
        return INACTIVE_STATE
      }
    } else {
      // Unquoted path - complete when any whitespace is encountered
      if (/\s/.test(query)) {
        return INACTIVE_STATE
      }
    }
  } else if (lastTriggerType === '#') {
    // For # triggers:
    // - Any whitespace in query means trigger is complete (PR numbers don't have spaces)
    if (/\s/.test(query)) {
      return INACTIVE_STATE
    }
  }

  return {
    active: true,
    type: lastTriggerType,
    query,
    triggerPosition: lastTriggerIndex,
  }
}

/**
 * React hook for detecting mention triggers in text input.
 *
 * @param value - The current input value
 * @param cursorPosition - The current cursor position
 * @returns MentionTriggerState
 */
export function useMentionTrigger(value: string, cursorPosition: number): MentionTriggerState {
  return useMemo(() => detectMentionTrigger(value, cursorPosition), [value, cursorPosition])
}
