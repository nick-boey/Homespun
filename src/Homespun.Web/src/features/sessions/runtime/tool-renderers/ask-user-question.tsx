import { useMemo, useState } from 'react'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { cn } from '@/lib/utils'

import { useSessionId } from '../session-context'

/**
 * Wire-shape the server's `A2AToAGUITranslator.BuildInputRequired` emits inside
 * the `ask_user_question` tool-call args JSON. Matches `PendingQuestion` in
 * `types/signalr.ts` minus id-generation concerns.
 */
export interface AskUserQuestionArgs {
  id?: string
  toolUseId?: string
  questions: Array<{
    question: string
    header?: string
    options?: Array<{ label: string; description?: string }>
    multiSelect?: boolean
  }>
}

export type AskUserQuestionResult = Record<string, string | string[]>

/**
 * Frontend tool renderer for `ask_user_question`. Each question is rendered as
 * a set of option buttons (multi-select = toggle group, single-select = click
 * to commit). Submitting calls `addResult` with a `{question: answer}` map and
 * dispatches the same payload over SignalR so the worker is unblocked.
 */
export function AskUserQuestionRenderer({
  args,
  result,
  addResult,
}: {
  args: AskUserQuestionArgs
  result?: AskUserQuestionResult | undefined
  addResult: (value: AskUserQuestionResult) => void
}) {
  const sessionId = useSessionId()
  const { methods, isConnected } = useClaudeCodeHub()
  const questions = useMemo(() => args.questions ?? [], [args.questions])

  // Receipt mode — the result is already committed; render chosen options only.
  if (result) {
    return (
      <Card className="bg-card/60">
        <CardHeader className="pb-2">
          <CardTitle className="text-muted-foreground text-sm font-medium">Answered</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3 text-sm">
          {questions.map((q, idx) => {
            const answer = result[q.question]
            const shown = Array.isArray(answer) ? answer.join(', ') : (answer ?? '—')
            return (
              <div key={idx} className="space-y-1">
                <div className="text-foreground font-medium">{q.header || q.question}</div>
                <div className="text-muted-foreground">{shown}</div>
              </div>
            )
          })}
        </CardContent>
      </Card>
    )
  }

  return (
    <AskUserQuestionInteractive
      questions={questions}
      canSubmit={Boolean(sessionId) && isConnected}
      onSubmit={async (answers) => {
        addResult(answers)
        if (sessionId && methods) {
          await methods.answerQuestion(sessionId, JSON.stringify(answers))
        }
      }}
    />
  )
}

function AskUserQuestionInteractive({
  questions,
  canSubmit,
  onSubmit,
}: {
  questions: AskUserQuestionArgs['questions']
  canSubmit: boolean
  onSubmit: (answers: AskUserQuestionResult) => Promise<void> | void
}) {
  const [selections, setSelections] = useState<Record<string, string | string[]>>({})
  const [isSubmitting, setIsSubmitting] = useState(false)

  const toggleSingle = (qKey: string, optLabel: string) =>
    setSelections((prev) => ({ ...prev, [qKey]: optLabel }))

  const toggleMulti = (qKey: string, optLabel: string) =>
    setSelections((prev) => {
      const current = Array.isArray(prev[qKey]) ? (prev[qKey] as string[]) : []
      const next = current.includes(optLabel)
        ? current.filter((v) => v !== optLabel)
        : [...current, optLabel]
      return { ...prev, [qKey]: next }
    })

  const allAnswered = questions.every((q) => {
    const v = selections[q.question]
    if (q.multiSelect) return Array.isArray(v) && v.length > 0
    return typeof v === 'string' && v.length > 0
  })

  const handleSubmit = async () => {
    if (!allAnswered || !canSubmit) return
    setIsSubmitting(true)
    try {
      await onSubmit(selections)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Card className="bg-card/60">
      <CardContent className="space-y-4 pt-4">
        {questions.map((q, idx) => (
          <div key={idx} className="space-y-2">
            {q.header && <div className="text-foreground text-sm font-medium">{q.header}</div>}
            <div className="text-muted-foreground text-sm">{q.question}</div>
            <div className="flex flex-wrap gap-2">
              {(q.options ?? []).map((opt) => {
                const active = q.multiSelect
                  ? Array.isArray(selections[q.question]) &&
                    (selections[q.question] as string[]).includes(opt.label)
                  : selections[q.question] === opt.label
                return (
                  <Button
                    key={opt.label}
                    type="button"
                    size="sm"
                    variant={active ? 'default' : 'outline'}
                    onClick={() =>
                      q.multiSelect
                        ? toggleMulti(q.question, opt.label)
                        : toggleSingle(q.question, opt.label)
                    }
                    className={cn('text-left')}
                    title={opt.description}
                  >
                    {opt.label}
                  </Button>
                )
              })}
            </div>
          </div>
        ))}
        <div className="flex items-center justify-end">
          <Button
            type="button"
            size="sm"
            onClick={handleSubmit}
            disabled={!allAnswered || !canSubmit || isSubmitting}
          >
            {isSubmitting ? 'Submitting…' : 'Submit'}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
