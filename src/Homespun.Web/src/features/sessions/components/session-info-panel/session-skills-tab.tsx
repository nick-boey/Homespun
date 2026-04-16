import type { ClaudeSession } from '@/types/signalr'
import { useProjectSkills } from '@/features/skills/hooks/use-project-skills'
import { Skeleton } from '@/components/ui/skeleton'
import { Sparkles } from 'lucide-react'

interface SessionSkillsTabProps {
  session: ClaudeSession
}

/**
 * Surfaces skills available to the session's project for Claude's
 * auto-invocation context. Renders skills from the `general` bucket —
 * OpenSpec + Homespun skills are already exposed in the dispatch dialogs.
 */
export function SessionSkillsTab({ session }: SessionSkillsTabProps) {
  const projectId = session.projectId ?? ''
  const { data, isLoading } = useProjectSkills(projectId)

  if (!projectId) {
    return <p className="text-muted-foreground text-sm">No project associated with this session.</p>
  }

  if (isLoading) {
    return (
      <div className="space-y-2">
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-12 w-full" />
      </div>
    )
  }

  const skills = data?.general ?? []

  if (skills.length === 0) {
    return <p className="text-muted-foreground text-sm">No skills available for this project.</p>
  }

  return (
    <div className="space-y-3">
      <p className="text-muted-foreground text-xs">
        Claude can auto-invoke these skills when the conversation matches their description.
      </p>
      <ul className="space-y-2">
        {skills.map((skill) => (
          <li
            key={skill.name ?? ''}
            className="border-border bg-card flex gap-2 rounded-md border p-3"
          >
            <Sparkles className="text-muted-foreground mt-0.5 h-4 w-4 flex-shrink-0" />
            <div className="min-w-0 flex-1">
              <div className="truncate font-mono text-sm font-medium">{skill.name}</div>
              {skill.description ? (
                <p className="text-muted-foreground mt-0.5 text-xs">{skill.description}</p>
              ) : null}
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
