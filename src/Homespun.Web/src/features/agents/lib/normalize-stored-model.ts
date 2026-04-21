import type { ClaudeModelInfo } from '@/api/generated/types.gen'

const TIER_ALIASES = ['opus', 'sonnet', 'haiku'] as const
type TierAlias = (typeof TIER_ALIASES)[number]

function isTierAlias(value: string): value is TierAlias {
  return (TIER_ALIASES as readonly string[]).includes(value)
}

function newestInTier(
  catalog: readonly ClaudeModelInfo[],
  tier: TierAlias
): ClaudeModelInfo | undefined {
  const matches = catalog.filter((m) => (m.id ?? '').toLowerCase().includes(tier))
  if (matches.length === 0) return undefined
  return matches.reduce((a, b) => (a.createdAt > b.createdAt ? a : b))
}

export function normalizeStoredModel(
  stored: string | null,
  catalog: readonly ClaudeModelInfo[],
  defaultModel: ClaudeModelInfo | null
): string | null {
  if (catalog.length === 0) return null

  if (stored) {
    const exact = catalog.find((m) => m.id === stored)
    if (exact?.id) return exact.id

    if (isTierAlias(stored)) {
      const tierMatch = newestInTier(catalog, stored)
      if (tierMatch?.id) return tierMatch.id
    }
  }

  return defaultModel?.id ?? catalog[0]?.id ?? null
}
