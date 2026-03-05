import type { ClaudeSession } from '@/api/generated'

interface SessionPrTabProps {
  session: ClaudeSession
}

export function SessionPrTab({ session }: SessionPrTabProps) {
  return (
    <div>
      <p>PR Tab - {session.entityId}</p>
    </div>
  )
}
