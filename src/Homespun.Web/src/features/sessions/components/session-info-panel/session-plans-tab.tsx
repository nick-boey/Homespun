import type { ClaudeSession } from '@/api/generated'

interface SessionPlansTabProps {
  session: ClaudeSession
}

export function SessionPlansTab({ session }: SessionPlansTabProps) {
  return (
    <div>
      <p>Plans Tab - {session.workingDirectory}</p>
    </div>
  )
}