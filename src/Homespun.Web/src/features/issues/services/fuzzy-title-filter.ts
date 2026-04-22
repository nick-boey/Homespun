/**
 * Case-insensitive substring match on issue titles. Used by the
 * orphan-link picker; deliberately simpler than `filter-query-parser`
 * (no field prefixes, no `me`, no `isNext:`).
 */
export function matchesTitleFilter(title: string | null | undefined, query: string): boolean {
  const q = query.trim().toLowerCase()
  if (!q) return true
  return (title ?? '').toLowerCase().includes(q)
}
