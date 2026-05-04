import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { Markdown } from '@/components/ui/markdown'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { Check, X, FileText } from 'lucide-react'

import { useSessionId } from '../session-context'

export interface ProposePlanArgs {
  planContent: string
  planFilePath?: string
}

export interface ProposePlanResult {
  approved: boolean
  keepContext: boolean
  feedback?: string | null
}

export function ProposePlanRenderer({
  args,
  result,
  addResult,
}: {
  args: ProposePlanArgs
  result?: ProposePlanResult | undefined
  addResult: (value: ProposePlanResult) => void
}) {
  const sessionId = useSessionId()
  const { methods, isConnected } = useClaudeCodeHub()

  if (result) {
    return <ProposePlanReceipt args={args} result={result} />
  }

  return (
    <ProposePlanInteractive
      args={args}
      canSubmit={Boolean(sessionId) && isConnected}
      onSubmit={async (decision) => {
        addResult(decision)
        if (sessionId && methods) {
          await methods.approvePlan(
            sessionId,
            decision.approved,
            decision.keepContext,
            decision.feedback ?? null
          )
        }
      }}
    />
  )
}

function ProposePlanReceipt({
  args,
  result,
}: {
  args: ProposePlanArgs
  result: ProposePlanResult
}) {
  return (
    <Card className="bg-card/60">
      <CardHeader className="pb-2">
        <CardTitle className="flex items-center gap-2 text-sm font-medium">
          {result.approved ? (
            <Check className="size-4 text-green-600 dark:text-green-400" />
          ) : (
            <X className="text-muted-foreground size-4" />
          )}
          <span>{result.approved ? 'Plan approved' : 'Plan rejected'}</span>
          {args.planFilePath && (
            <span className="text-muted-foreground flex items-center gap-1 text-xs font-normal">
              <FileText className="size-3" />
              {args.planFilePath.split('/').pop()}
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2 text-sm">
        <Markdown>{args.planContent}</Markdown>
        {result.feedback && (
          <div className="border-border text-muted-foreground mt-2 border-t pt-2">
            <span className="font-medium">Feedback:</span> {result.feedback}
          </div>
        )}
        {result.approved && (
          <div className="text-muted-foreground text-xs">
            {result.keepContext ? 'Context preserved.' : 'Context cleared before execution.'}
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function ProposePlanInteractive({
  args,
  canSubmit,
  onSubmit,
}: {
  args: ProposePlanArgs
  canSubmit: boolean
  onSubmit: (decision: ProposePlanResult) => Promise<void> | void
}) {
  const [keepContext, setKeepContext] = useState(true)
  const [feedback, setFeedback] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)

  const commit = async (approved: boolean) => {
    if (!canSubmit || isSubmitting) return
    setIsSubmitting(true)
    try {
      await onSubmit({
        approved,
        keepContext: approved ? keepContext : false,
        feedback: approved ? null : feedback.trim() || null,
      })
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Card className="bg-card/60" data-testid="propose-plan-card">
      <CardHeader className="pb-2">
        <CardTitle className="flex items-center gap-2 text-sm font-medium">
          <span>Plan awaiting approval</span>
          {args.planFilePath && (
            <span className="text-muted-foreground flex items-center gap-1 text-xs font-normal">
              <FileText className="size-3" />
              {args.planFilePath.split('/').pop()}
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <Markdown>{args.planContent}</Markdown>

        <div className="flex items-center justify-between gap-2 rounded-md border px-3 py-2">
          <label htmlFor="keep-context" className="text-sm">
            Keep context on approve
          </label>
          <Switch id="keep-context" checked={keepContext} onCheckedChange={setKeepContext} />
        </div>

        <Textarea
          placeholder="Optional feedback if rejecting the plan…"
          value={feedback}
          onChange={(e) => setFeedback(e.target.value)}
          className="min-h-[72px] text-sm"
          data-testid="propose-plan-feedback"
        />

        <div className="flex items-center justify-end gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => commit(false)}
            disabled={!canSubmit || isSubmitting}
            data-testid="propose-plan-reject"
          >
            Reject
          </Button>
          <Button
            type="button"
            size="sm"
            onClick={() => commit(true)}
            disabled={!canSubmit || isSubmitting}
            data-testid="propose-plan-approve"
          >
            {isSubmitting ? 'Submitting…' : 'Approve'}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
