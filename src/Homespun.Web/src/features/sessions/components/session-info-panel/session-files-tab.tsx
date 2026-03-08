import { FileText, FilePlus, FileX, FileDiff, FileCode } from 'lucide-react'
import type { ClaudeSession, FileChangeInfo } from '@/api/generated'
import { useChangedFiles } from '@/features/sessions/hooks'
import { cn } from '@/lib/utils'
import { Skeleton } from '@/components/ui/skeleton'

interface SessionFilesTabProps {
  session: ClaudeSession
}

// Map numeric status to meaningful labels and icons
const getFileStatusInfo = (status?: number) => {
  switch (status) {
    case 0: // Added
      return {
        icon: FilePlus,
        label: 'Added',
        color: 'text-green-600 dark:text-green-400',
        bgColor: 'bg-green-500/10',
        borderColor: 'border-green-500/20',
      }
    case 1: // Modified
      return {
        icon: FileDiff,
        label: 'Modified',
        color: 'text-yellow-600 dark:text-yellow-400',
        bgColor: 'bg-yellow-500/10',
        borderColor: 'border-yellow-500/20',
      }
    case 2: // Deleted
      return {
        icon: FileX,
        label: 'Deleted',
        color: 'text-red-600 dark:text-red-400',
        bgColor: 'bg-red-500/10',
        borderColor: 'border-red-500/20',
      }
    case 3: // Renamed
      return {
        icon: FileCode,
        label: 'Renamed',
        color: 'text-cyan-600 dark:text-cyan-400',
        bgColor: 'bg-cyan-500/10',
        borderColor: 'border-cyan-500/20',
      }
    default:
      return {
        icon: FileText,
        label: 'Unknown',
        color: 'text-muted-foreground',
        bgColor: 'bg-muted/10',
        borderColor: 'border-muted/20',
      }
  }
}

export function SessionFilesTab({ session }: SessionFilesTabProps) {
  const { data: files, isLoading, isError } = useChangedFiles(session)

  if (!session.workingDirectory) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <FileText className="mb-3 h-12 w-12 opacity-50" />
        <p>No working directory for this session</p>
      </div>
    )
  }

  if (isLoading) {
    return (
      <div className="space-y-2">
        {[1, 2, 3].map((i) => (
          <div key={i} className="rounded-lg border p-3">
            <div className="flex items-start gap-3">
              <Skeleton className="h-5 w-5 shrink-0" />
              <div className="flex-1 space-y-2">
                <Skeleton className="h-4 w-full" />
                <Skeleton className="h-3 w-24" />
              </div>
            </div>
          </div>
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <FileText className="mb-3 h-12 w-12 opacity-50" />
        <p>Failed to load changed files</p>
      </div>
    )
  }

  if (!files || files.length === 0) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <FileText className="mb-3 h-12 w-12 opacity-50" />
        <p>No file changes in this session</p>
      </div>
    )
  }

  // Group files by status
  const groupedFiles = files.reduce<Record<number, FileChangeInfo[]>>((acc, file) => {
    const status = file.status ?? -1
    if (!acc[status]) {
      acc[status] = []
    }
    acc[status].push(file)
    return acc
  }, {})

  return (
    <div className="space-y-4">
      {/* Summary */}
      <div className="text-muted-foreground text-sm">
        {files.length} file{files.length !== 1 ? 's' : ''} changed
      </div>

      {/* Files grouped by status */}
      {Object.entries(groupedFiles).map(([status, statusFiles]) => {
        const statusInfo = getFileStatusInfo(Number(status))
        const Icon = statusInfo.icon

        return (
          <div key={status} className="space-y-2">
            <div className="flex items-center gap-2">
              <Icon className={cn('h-4 w-4', statusInfo.color)} />
              <span className="text-muted-foreground text-xs font-medium">
                {statusInfo.label} ({statusFiles.length})
              </span>
            </div>

            <div className="space-y-2">
              {statusFiles.map((file, index) => (
                <div
                  key={`${file.filePath}-${index}`}
                  className={cn(
                    'rounded-lg border p-3',
                    statusInfo.bgColor,
                    statusInfo.borderColor
                  )}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="flex-1">
                      <p className="font-mono text-sm break-all">{file.filePath}</p>
                      {(file.additions !== undefined || file.deletions !== undefined) && (
                        <div className="mt-1 flex items-center gap-3 text-xs">
                          {file.additions !== undefined && file.additions > 0 && (
                            <span className="text-green-600 dark:text-green-400">
                              +{file.additions}
                            </span>
                          )}
                          {file.deletions !== undefined && file.deletions > 0 && (
                            <span className="text-red-600 dark:text-red-400">
                              -{file.deletions}
                            </span>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )
      })}

      {/* Total stats */}
      {files.some((f) => f.additions !== undefined || f.deletions !== undefined) && (
        <div className="border-t pt-3">
          <div className="text-muted-foreground flex items-center gap-4 text-sm">
            <span>Total:</span>
            <span className="text-green-600 dark:text-green-400">
              +{files.reduce((sum, f) => sum + (f.additions || 0), 0)}
            </span>
            <span className="text-red-600 dark:text-red-400">
              -{files.reduce((sum, f) => sum + (f.deletions || 0), 0)}
            </span>
          </div>
        </div>
      )}
    </div>
  )
}
