import { Terminal } from 'lucide-react'

export function SessionsEmptyState() {
  return (
    <div className="flex flex-col items-center justify-center rounded-lg border border-dashed p-12 text-center">
      <Terminal className="text-muted-foreground/50 h-12 w-12" />
      <h3 className="mt-4 text-lg font-semibold">No sessions yet</h3>
      <p className="text-muted-foreground mt-2 text-sm">
        Sessions will appear here when you start working with AI agents on your projects.
      </p>
    </div>
  )
}
