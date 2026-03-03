import { useState } from 'react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { Markdown } from '@/components/ui/markdown'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import {
  ClipboardList,
  RefreshCw,
  Play,
  X,
  ChevronDown,
  ChevronRight,
  FileText,
} from 'lucide-react'

export interface PlanApprovalPanelProps {
  /** The plan content in markdown format */
  planContent: string
  /** Optional file path where the plan was written */
  planFilePath?: string
  /** Callback when user clicks "Clear Context & Start Implementation" */
  onApproveClearContext: () => void
  /** Callback when user clicks "Continue with Context" */
  onApproveKeepContext: () => void
  /** Callback when user clicks "Send Feedback" with rejection feedback */
  onReject: (feedback: string) => void
  /** Whether an action is in progress */
  isLoading?: boolean
  /** Error message to display */
  error?: string
  /** Additional CSS classes */
  className?: string
}

/**
 * Panel for approving or rejecting a plan created by Claude.
 *
 * Displays:
 * - Header with title and icon
 * - Description of available options
 * - Three action buttons for approval/rejection
 * - Expandable feedback textarea for rejection
 * - Collapsible view of the plan content
 */
export function PlanApprovalPanel({
  planContent,
  planFilePath,
  onApproveClearContext,
  onApproveKeepContext,
  onReject,
  isLoading = false,
  error,
  className,
}: PlanApprovalPanelProps) {
  const [showFeedback, setShowFeedback] = useState(false)
  const [feedback, setFeedback] = useState('')
  const [isPlanExpanded, setIsPlanExpanded] = useState(false)

  const handleReject = () => {
    onReject(feedback)
  }

  const toggleFeedback = () => {
    setShowFeedback(!showFeedback)
  }

  return (
    <div className={cn('bg-card border-border rounded-lg border p-4 shadow-sm', className)}>
      {/* Header */}
      <div className="mb-3 flex items-center gap-2">
        <ClipboardList className="text-primary h-5 w-5" />
        <h3 className="text-lg font-semibold">Plan Ready for Implementation</h3>
      </div>

      {/* Description */}
      <p className="text-muted-foreground mb-4 text-sm">
        Choose how to proceed with the implementation:
      </p>

      {/* Action buttons */}
      <div className="mb-4 flex flex-wrap gap-2">
        <Button onClick={onApproveClearContext} disabled={isLoading} className="gap-2">
          <RefreshCw className="h-4 w-4" />
          Clear Context & Start Implementation
        </Button>

        <Button
          variant="secondary"
          onClick={onApproveKeepContext}
          disabled={isLoading}
          className="gap-2"
        >
          <Play className="h-4 w-4" />
          Continue with Context
        </Button>

        <Button
          variant="destructive"
          onClick={toggleFeedback}
          disabled={isLoading}
          className="gap-2"
        >
          <X className="h-4 w-4" />
          Reject & Modify
        </Button>
      </div>

      {/* Feedback textarea (shown when Reject & Modify is clicked) */}
      {showFeedback && (
        <div className="bg-muted/30 mb-4 rounded-md border p-3">
          <Textarea
            value={feedback}
            onChange={(e) => setFeedback(e.target.value)}
            placeholder="Describe what changes you'd like to the plan..."
            className="mb-2 min-h-[80px]"
            disabled={isLoading}
          />
          <Button variant="destructive" onClick={handleReject} disabled={isLoading} size="sm">
            Send Feedback
          </Button>
        </div>
      )}

      {/* Error message */}
      {error && (
        <div
          role="alert"
          className="bg-destructive/10 text-destructive mb-4 rounded-md border border-red-200 p-3 text-sm"
        >
          {error}
        </div>
      )}

      {/* Plan content (collapsible) */}
      <Collapsible open={isPlanExpanded} onOpenChange={setIsPlanExpanded}>
        <CollapsibleTrigger asChild>
          <Button
            variant="ghost"
            size="sm"
            className="text-muted-foreground hover:text-foreground gap-1 p-0"
          >
            {isPlanExpanded ? (
              <ChevronDown className="h-4 w-4" />
            ) : (
              <ChevronRight className="h-4 w-4" />
            )}
            View Plan
          </Button>
        </CollapsibleTrigger>
        <CollapsibleContent className="mt-2">
          {planFilePath && (
            <div className="text-muted-foreground mb-2 flex items-center gap-1 text-xs">
              <FileText className="h-3 w-3" />
              <span>{planFilePath.split('/').pop()}</span>
            </div>
          )}
          <div className="bg-muted/50 max-h-[400px] overflow-y-auto rounded-md border p-4">
            <Markdown className="prose prose-sm dark:prose-invert max-w-none">
              {planContent}
            </Markdown>
          </div>
        </CollapsibleContent>
      </Collapsible>
    </div>
  )
}
