/**
 * Sort-order midpoint computation matching Fleece.Core's LexoRank.GetMiddleRank.
 *
 * Fleece.Core uses 'a'–'z' as its rank alphabet. Given two rank strings
 * `prev` and `next` where `prev < next` under ordinal comparison,
 * `midpoint(prev, next)` returns a string `m` such that `prev < m < next`.
 *
 * Boundary sentinels (matching C# null semantics):
 * - `prev = ""` → "no lower bound": equivalent to C# GetMiddleRank(null, next) → GetRankBefore(next)
 * - `next = ""` → "no upper bound": equivalent to C# GetMiddleRank(prev, null) → GetRankAfter(prev)
 * - `prev === next` → throws (caller bug)
 */

const ALPHABET = 'abcdefghijklmnopqrstuvwxyz'

// Exported for use in unit tests
export const SORT_FIRST = ALPHABET.charCodeAt(0) // 97 = 'a'
export const SORT_LAST = ALPHABET.charCodeAt(ALPHABET.length - 1) // 122 = 'z'

function getRankBefore(after: string): string {
  const chars = after.split('')
  for (let i = chars.length - 1; i >= 0; i--) {
    const idx = ALPHABET.indexOf(chars[i])
    if (idx > 0) {
      chars[i] = ALPHABET[Math.floor(idx / 2)]
      return chars.join('')
    }
  }
  return 'a' + 'n'.repeat(after.length)
}

function getRankAfter(before: string): string {
  const chars = before.split('')
  for (let i = chars.length - 1; i >= 0; i--) {
    const idx = ALPHABET.indexOf(chars[i])
    if (idx < ALPHABET.length - 1) {
      chars[i] = ALPHABET[Math.floor((idx + ALPHABET.length) / 2)]
      return chars.join('')
    }
  }
  return before + 'n'
}

function getRankBetween(before: string, after: string): string {
  const len = Math.max(before.length, after.length)
  const text = before.padEnd(len, 'a')
  const text2 = after.padEnd(len, 'a')
  const arr: string[] = new Array(len)

  for (let i = 0; i < len; i++) {
    const idx2 = ALPHABET.indexOf(text[i])
    const idx3 = ALPHABET.indexOf(text2[i])

    if (idx2 < idx3 - 1) {
      arr[i] = ALPHABET[Math.floor((idx2 + idx3) / 2)]
      for (let j = i + 1; j < len; j++) {
        arr[j] = text[j]
      }
      // Mirror C#: .TrimEnd('a').PadRight(3, 'a')
      return arr.join('').replace(/a+$/, '').padEnd(3, 'a')
    }

    arr[i] = text[i]
  }

  return before + 'n'
}

/**
 * Returns a string strictly between `prev` and `next` under ordinal-byte
 * comparison, using Fleece.Core's 'a'–'z' rank alphabet.
 *
 * - `prev = ""` is a sentinel for "no lower bound" (maps to getRankBefore)
 * - `next = ""` is a sentinel for "no upper bound" (maps to getRankAfter)
 * - `prev === next` throws
 */
export function midpoint(prev: string, next: string): string {
  if (prev === next) {
    throw new Error(`midpoint: called with equal strings ${JSON.stringify(prev)}`)
  }

  if (prev === '') {
    return getRankBefore(next)
  }

  if (next === '') {
    return getRankAfter(prev)
  }

  return getRankBetween(prev, next)
}
