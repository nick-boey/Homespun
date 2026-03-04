/**
 * Utility for computing lexicographic midpoints between sort order strings.
 * Used to insert new sibling issues between existing ones in series-mode parents.
 *
 * This is a TypeScript port of LexOrderUtils.cs
 */

const MIN_CHAR = '!' // ASCII 33 - below all printable sort order chars
const MAX_CHAR = 'z'
const MID_CHAR = 'V'

/**
 * Computes a string that sorts lexicographically between `before` and `after`.
 *
 * @param before - The lower bound (exclusive), or null/undefined if inserting before all.
 * @param after - The upper bound (exclusive), or null/undefined if inserting after all.
 * @returns A string that is lexicographically between before and after.
 */
export function computeMidpoint(before?: string | null, after?: string | null): string {
  if (!before && !after) {
    return MID_CHAR
  }

  if (!before) {
    // Insert before 'after': prepend a character that sorts before after's first char
    // If after starts with MIN_CHAR, we need to go deeper
    if (after!.length > 0 && after![0] > MIN_CHAR) {
      const midCharCode = Math.floor((MIN_CHAR.charCodeAt(0) + after![0].charCodeAt(0)) / 2)
      const midChar = String.fromCharCode(midCharCode)
      if (midChar > MIN_CHAR && midChar < after![0]) {
        return midChar
      }
    }
    // Fallback: prepend MIN_CHAR and recurse
    return MIN_CHAR + computeMidpoint(null, after!.length > 0 ? after!.slice(1) : null)
  }

  if (!after) {
    // Insert after 'before': append MID_CHAR
    return before + MID_CHAR
  }

  // Both bounds present: find midpoint between them
  return computeMidpointBetween(before, after)
}

function computeMidpointBetween(a: string, b: string): string {
  // Pad shorter string with MIN_CHAR conceptually
  const maxLen = Math.max(a.length, b.length)

  // Try character-by-character midpoint
  for (let i = 0; i < maxLen; i++) {
    const ca = i < a.length ? a[i] : MIN_CHAR
    const cb = i < b.length ? b[i] : MAX_CHAR

    if (ca < cb) {
      const midCode = Math.floor((ca.charCodeAt(0) + cb.charCodeAt(0)) / 2)
      const mid = String.fromCharCode(midCode)
      if (mid > ca) {
        // Found a valid midpoint at this position
        const prefix = a.slice(0, i)
        return prefix + mid
      }
      // ca and cb are adjacent - need to go deeper
      // Take ca and find midpoint in the remaining space
      return a.slice(0, i + 1) + computeMidpoint(i + 1 < a.length ? a.slice(i + 1) : null, null)
    }

    if (ca === cb) {
      continue // Same character, move to next position
    }

    // ca > cb shouldn't happen if a < b, but handle gracefully
    break
  }

  // Strings are equal up to maxLen - append MID_CHAR to the shorter one
  return a + MID_CHAR
}
