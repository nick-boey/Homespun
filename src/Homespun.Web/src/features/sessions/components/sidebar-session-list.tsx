import { useMemo } from 'react'
import { TooltipProvider } from '@/components/ui/tooltip'
import { useAllSessions, useGroupedSessionsByProject } from '../hooks/use-all-sessions'
import { useEnrichedSessions } from '../hooks/use-enriched-sessions'
import { SidebarSessionRow } from './sidebar-session-row'

interface SidebarSessionListProps {
  projectId: string
  onNavigate?: () => void
}

/**
 * Renders the running sessions for a single project in the sidebar.
 *
 * The list itself is driven by `useAllSessions()` so SignalR-invalidated
 * cache events flow through automatically. Entity titles are resolved via
 * `useEnrichedSessions()` whose parallel queries are cached for the duration
 * of the page load. Returns `null` when the project has zero non-`STOPPED`
 * sessions.
 */
export function SidebarSessionList({ projectId, onNavigate }: SidebarSessionListProps) {
  const { data: allSessions } = useAllSessions()
  const grouped = useGroupedSessionsByProject(allSessions)
  const projectSessions = grouped.get(projectId)

  const { sessions: enriched } = useEnrichedSessions()
  const titleBySessionId = useMemo(() => {
    const map = new Map<string, string>()
    for (const e of enriched) {
      const id = e.session.id
      if (!id) continue
      const title = e.entityTitle ?? e.session.entityId ?? id
      map.set(id, title)
    }
    return map
  }, [enriched])

  if (!projectSessions || projectSessions.length === 0) {
    return null
  }

  return (
    <TooltipProvider>
      <div className="space-y-0.5">
        {projectSessions.map((session) => {
          const id = session.id
          if (!id) return null
          const title = titleBySessionId.get(id) ?? session.entityId ?? id
          return (
            <SidebarSessionRow key={id} session={session} title={title} onNavigate={onNavigate} />
          )
        })}
      </div>
    </TooltipProvider>
  )
}
