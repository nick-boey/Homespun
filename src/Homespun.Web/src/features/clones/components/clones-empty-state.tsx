import { FolderOpen } from 'lucide-react'

export function ClonesEmptyState() {
  return (
    <div className="flex flex-col items-center justify-center py-12 text-center">
      <FolderOpen className="text-muted-foreground mb-4 h-12 w-12" />
      <h3 className="text-lg font-medium">No Clones Found</h3>
      <p className="text-muted-foreground mt-1">
        Clones will appear here when you create sessions on branches.
      </p>
    </div>
  )
}
