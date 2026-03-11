import { useState } from 'react'
import { FileText, ChevronDown, ChevronRight, Copy, Check } from 'lucide-react'
import type { PlanFileInfo } from '@/api/generated'
import type { ClaudeSession } from '@/types/signalr'
import { usePlanFiles, usePlanContent } from '@/features/sessions/hooks'
import { Skeleton } from '@/components/ui/skeleton'

interface SessionPlansTabProps {
  session: ClaudeSession
}

interface PlanItemProps {
  plan: PlanFileInfo
  workingDirectory: string
}

function formatFileSize(bytes?: number): string {
  if (!bytes) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${(bytes / Math.pow(k, i)).toFixed(1)} ${sizes[i]}`
}

function formatRelativeTime(dateString?: string): string {
  if (!dateString) return 'Unknown'
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / (1000 * 60))
  const diffHours = Math.floor(diffMs / (1000 * 60 * 60))
  const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24))

  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins} minute${diffMins !== 1 ? 's' : ''} ago`
  if (diffHours < 24) return `${diffHours} hour${diffHours !== 1 ? 's' : ''} ago`
  return `${diffDays} day${diffDays !== 1 ? 's' : ''} ago`
}

function PlanItem({ plan, workingDirectory }: PlanItemProps) {
  const [isExpanded, setIsExpanded] = useState(false)
  const [copiedPath, setCopiedPath] = useState(false)
  const [copiedContent, setCopiedContent] = useState(false)
  const { data: content, isLoading } = usePlanContent(
    isExpanded && workingDirectory ? workingDirectory : undefined,
    isExpanded && plan.fileName ? plan.fileName : undefined
  )

  const handleCopy = async (text: string, setCopied: (value: boolean) => void) => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch (error) {
      console.error('Failed to copy:', error)
    }
  }

  return (
    <div className="rounded-lg border">
      <div
        className="hover:bg-accent/50 w-full cursor-pointer p-3 text-left transition-colors"
        onClick={() => setIsExpanded(!isExpanded)}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault()
            setIsExpanded(!isExpanded)
          }
        }}
      >
        <div className="flex items-start justify-between gap-2">
          <div className="flex flex-1 items-start gap-2">
            {isExpanded ? (
              <ChevronDown className="mt-0.5 h-4 w-4 shrink-0" />
            ) : (
              <ChevronRight className="mt-0.5 h-4 w-4 shrink-0" />
            )}
            <div className="flex-1">
              <p className="font-mono text-sm font-medium break-all">{plan.fileName}</p>
              <div className="text-muted-foreground mt-1 flex items-center gap-3 text-xs">
                <span>{formatRelativeTime(plan.lastModified)}</span>
                <span>{formatFileSize(plan.fileSizeBytes)}</span>
              </div>
              {plan.preview && !isExpanded && (
                <p className="text-muted-foreground mt-2 line-clamp-2 text-xs">{plan.preview}</p>
              )}
            </div>
          </div>
          <button
            className="hover:bg-accent relative z-10 shrink-0 rounded p-1"
            onClick={(e) => {
              e.stopPropagation()
              handleCopy(plan.filePath || plan.fileName || '', setCopiedPath)
            }}
          >
            {copiedPath ? (
              <Check className="h-3 w-3 text-green-600" />
            ) : (
              <Copy className="text-muted-foreground h-3 w-3" />
            )}
          </button>
        </div>
      </div>

      {isExpanded && (
        <div className="border-t">
          {isLoading ? (
            <div className="space-y-2 p-3">
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-3/4" />
            </div>
          ) : content ? (
            <div>
              <div className="bg-muted/50 flex items-center justify-between border-b px-3 py-2">
                <span className="text-muted-foreground text-xs">Plan content</span>
                <button
                  className="hover:bg-accent rounded p-1"
                  onClick={() => handleCopy(content, setCopiedContent)}
                >
                  {copiedContent ? (
                    <Check className="h-3 w-3 text-green-600" />
                  ) : (
                    <Copy className="text-muted-foreground h-3 w-3" />
                  )}
                </button>
              </div>
              <div className="p-3">
                <pre className="text-muted-foreground overflow-x-auto font-mono text-xs whitespace-pre-wrap">
                  {content}
                </pre>
              </div>
            </div>
          ) : (
            <div className="text-muted-foreground p-3 text-sm">Failed to load plan content</div>
          )}
        </div>
      )}
    </div>
  )
}

export function SessionPlansTab({ session }: SessionPlansTabProps) {
  const { data: plans, isLoading, isError } = usePlanFiles(session)
  const workingDirectory = session.workingDirectory

  if (!workingDirectory) {
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
            <div className="space-y-2">
              <Skeleton className="h-4 w-48" />
              <Skeleton className="h-3 w-32" />
              <Skeleton className="h-3 w-full" />
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
        <p>Failed to load plan files</p>
      </div>
    )
  }

  if (!plans || plans.length === 0) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <FileText className="mb-3 h-12 w-12 opacity-50" />
        <p>No plan files in this session</p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Summary */}
      <div className="text-muted-foreground text-sm">
        {plans.length} plan file{plans.length !== 1 ? 's' : ''} found
      </div>

      {/* Plan files */}
      <div className="space-y-2">
        {plans.map((plan, index) => (
          <PlanItem
            key={`${plan.fileName}-${index}`}
            plan={plan}
            workingDirectory={workingDirectory}
          />
        ))}
      </div>
    </div>
  )
}
