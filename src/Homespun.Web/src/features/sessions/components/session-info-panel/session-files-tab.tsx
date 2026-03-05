import type { ClaudeSession } from '@/api/generated'

interface SessionFilesTabProps {
  session: ClaudeSession
}

export function SessionFilesTab({ session }: SessionFilesTabProps) {
  return (
    <div>
      <p>Files Tab - {session.workingDirectory}</p>
    </div>
  )
}
