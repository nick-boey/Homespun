import { Link } from '@tanstack/react-router'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import { getSessionStatusColor } from '../utils/session-status-color'
import type { SessionSummary } from '@/api/generated/types.gen'

interface SidebarSessionRowProps {
  session: SessionSummary
  title: string
  isActive?: boolean
  onNavigate?: () => void
}

/**
 * Compact session row rendered under each project in the sidebar.
 *
 * Layout: status colour dot + truncated title. Hover tooltip carries the
 * full title. Clicking navigates to `/sessions/$sessionId`.
 */
export function SidebarSessionRow({
  session,
  title,
  isActive,
  onNavigate,
}: SidebarSessionRowProps) {
  const colorClass = getSessionStatusColor(session.status)
  const sessionId = session.id

  if (!sessionId || !colorClass) {
    return null
  }

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Link
          to="/sessions/$sessionId"
          params={{ sessionId }}
          onClick={onNavigate}
          data-testid={`sidebar-session-${sessionId}`}
          aria-current={isActive ? 'page' : undefined}
          className={cn(
            'flex min-h-[36px] items-center gap-2 rounded-md py-1.5 pr-3 pl-9 text-sm font-medium transition-colors',
            'hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
            'active:bg-sidebar-accent/80',
            isActive
              ? 'bg-sidebar-accent text-sidebar-accent-foreground'
              : 'text-sidebar-foreground/90'
          )}
        >
          <span
            data-testid={`sidebar-session-${sessionId}-dot`}
            className={cn('h-2 w-2 shrink-0 rounded-full', colorClass)}
          />
          <span className="truncate">{title}</span>
        </Link>
      </TooltipTrigger>
      <TooltipContent side="right">{title}</TooltipContent>
    </Tooltip>
  )
}
