import { ClaudeSessionStatus } from '@/api'

/**
 * Single source of truth for the session status → colour family mapping
 * used by the top-bar `ActiveAgentsIndicator` and the sidebar session rows.
 *
 * `null` for `STOPPED` / unmapped (never rendered).
 */
export type SessionStatusColorName = 'green' | 'yellow' | 'purple' | 'orange' | 'red'

const STATUS_TO_COLOR: Record<ClaudeSessionStatus, SessionStatusColorName | null> = {
  [ClaudeSessionStatus.STARTING]: 'green',
  [ClaudeSessionStatus.RUNNING_HOOKS]: 'green',
  [ClaudeSessionStatus.RUNNING]: 'green',
  [ClaudeSessionStatus.WAITING_FOR_INPUT]: 'yellow',
  [ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER]: 'purple',
  [ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION]: 'orange',
  [ClaudeSessionStatus.ERROR]: 'red',
  [ClaudeSessionStatus.STOPPED]: null,
}

const BG_CLASS: Record<SessionStatusColorName, string> = {
  green: 'bg-green-500',
  yellow: 'bg-yellow-500',
  purple: 'bg-purple-500',
  orange: 'bg-orange-500',
  red: 'bg-red-500',
}

const TEXT_CLASS: Record<SessionStatusColorName, string> = {
  green: 'text-green-500',
  yellow: 'text-yellow-500',
  purple: 'text-purple-500',
  orange: 'text-orange-500',
  red: 'text-red-500',
}

export function getSessionStatusColorName(
  status: ClaudeSessionStatus | undefined
): SessionStatusColorName | null {
  if (!status) return null
  return STATUS_TO_COLOR[status] ?? null
}

/**
 * Returns the Tailwind background class for the dot rendered next to a
 * session row, or `null` when the status should not be rendered.
 */
export function getSessionStatusColor(status: ClaudeSessionStatus | undefined): string | null {
  const name = getSessionStatusColorName(status)
  return name ? BG_CLASS[name] : null
}

/**
 * Returns the Tailwind text/foreground class for surfaces that colour an
 * icon (e.g. the top-bar indicator), backed by the same colour-name source.
 */
export function getSessionStatusTextColor(status: ClaudeSessionStatus | undefined): string | null {
  const name = getSessionStatusColorName(status)
  return name ? TEXT_CLASS[name] : null
}
