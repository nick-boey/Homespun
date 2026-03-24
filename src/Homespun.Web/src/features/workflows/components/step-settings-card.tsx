import type { WorkflowStep, AgentPrompt } from '@/api/generated/types.gen'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Button } from '@/components/ui/button'
import { useState } from 'react'

interface StepSettingsCardProps {
  step: WorkflowStep
  allSteps: WorkflowStep[]
  prompts: AgentPrompt[]
  projectId: string
  onChange: (updated: WorkflowStep) => void
}

const STEP_TYPES = [
  { value: 'agent' as const, label: 'Agent' },
  { value: 'serverAction' as const, label: 'Server Action' },
  { value: 'gate' as const, label: 'Gate' },
]

const SUCCESS_TRANSITIONS = [
  { value: 'nextStep', label: 'Next Step' },
  { value: 'goToStep', label: 'Go To Step' },
  { value: 'exit', label: 'Exit Success' },
]

const FAILURE_TRANSITIONS = [
  { value: 'retry', label: 'Retry' },
  { value: 'goToStep', label: 'Go To Step' },
  { value: 'exit', label: 'Exit Fail' },
]

type PromptMode = 'inline' | 'template'

function getInitialPromptMode(step: WorkflowStep): PromptMode {
  if (step.promptId) return 'template'
  return 'inline'
}

export function StepSettingsCard({ step, allSteps, prompts, onChange }: StepSettingsCardProps) {
  const [promptMode, setPromptMode] = useState<PromptMode>(getInitialPromptMode(step))
  const otherSteps = allSteps.filter((s) => s.id !== step.id)

  function update(partial: Partial<WorkflowStep>) {
    onChange({ ...step, ...partial })
  }

  function getConfig(): Record<string, unknown> {
    if (step.config && typeof step.config === 'object') {
      return step.config as Record<string, unknown>
    }
    return {}
  }

  function updateConfig(partial: Record<string, unknown>) {
    update({ config: { ...getConfig(), ...partial } })
  }

  return (
    <Card data-testid="step-settings-card">
      <CardHeader>
        <CardTitle className="text-base">Step Settings</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Step Name */}
        <div className="space-y-1">
          <Label htmlFor="step-name">Name</Label>
          <Input
            id="step-name"
            data-testid="step-name-input"
            value={step.name ?? ''}
            onChange={(e) => update({ name: e.target.value })}
          />
        </div>

        {/* Step Type */}
        <div className="space-y-1">
          <Label>Type</Label>
          <div className="flex gap-1">
            {STEP_TYPES.map((t) => (
              <Button
                key={t.value}
                data-testid={`step-type-${t.value}`}
                variant={step.stepType === t.value ? 'default' : 'outline'}
                size="sm"
                onClick={() => update({ stepType: t.value })}
              >
                {t.label}
              </Button>
            ))}
          </div>
        </div>

        {/* Agent Config */}
        {step.stepType === 'agent' && (
          <div className="space-y-3" data-testid="agent-config">
            {/* Prompt Mode Toggle */}
            <div className="space-y-1">
              <Label>Prompt</Label>
              <div className="flex gap-1">
                <Button
                  data-testid="prompt-mode-inline"
                  variant={promptMode === 'inline' ? 'default' : 'outline'}
                  size="sm"
                  onClick={() => {
                    setPromptMode('inline')
                    update({ promptId: null })
                  }}
                >
                  Inline Prompt
                </Button>
                <Button
                  data-testid="prompt-mode-template"
                  variant={promptMode === 'template' ? 'default' : 'outline'}
                  size="sm"
                  onClick={() => {
                    setPromptMode('template')
                    update({ prompt: null })
                  }}
                >
                  Use Template
                </Button>
              </div>
            </div>

            {promptMode === 'inline' ? (
              <div className="space-y-1">
                <Textarea
                  data-testid="prompt-textarea"
                  placeholder="Enter prompt... Use {{variable}} for template variables"
                  value={step.prompt ?? ''}
                  onChange={(e) => update({ prompt: e.target.value })}
                  rows={4}
                />
              </div>
            ) : (
              <div className="space-y-1">
                <select
                  data-testid="prompt-template-select"
                  className="border-input bg-background ring-offset-background flex h-9 w-full rounded-md border px-3 py-1 text-sm"
                  value={step.promptId ?? ''}
                  onChange={(e) => update({ promptId: e.target.value || null })}
                >
                  <option value="">Select a template...</option>
                  {prompts.map((p) => (
                    <option key={p.id} value={p.id ?? ''}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </div>
            )}

            {/* Session Mode */}
            <div className="space-y-1">
              <Label>Session Mode</Label>
              <select
                data-testid="session-mode-select"
                className="border-input bg-background ring-offset-background flex h-9 w-full rounded-md border px-3 py-1 text-sm"
                value={step.sessionMode ?? 'build'}
                onChange={(e) => update({ sessionMode: e.target.value as 'plan' | 'build' })}
              >
                <option value="plan">Plan</option>
                <option value="build">Build</option>
              </select>
            </div>
          </div>
        )}

        {/* Gate Config */}
        {step.stepType === 'gate' && (
          <div className="space-y-3" data-testid="gate-config">
            <div className="space-y-1">
              <Label htmlFor="gate-description">Description</Label>
              <Textarea
                id="gate-description"
                data-testid="gate-description-input"
                placeholder="What needs to be approved?"
                value={(getConfig().description as string) ?? ''}
                onChange={(e) => updateConfig({ description: e.target.value })}
                rows={2}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="gate-timeout">Timeout (seconds)</Label>
              <Input
                id="gate-timeout"
                data-testid="gate-timeout-input"
                type="number"
                value={(getConfig().timeoutSeconds as number) ?? 3600}
                onChange={(e) => updateConfig({ timeoutSeconds: parseInt(e.target.value) || 3600 })}
              />
            </div>
          </div>
        )}

        {/* Server Action Config */}
        {step.stepType === 'serverAction' && (
          <div className="space-y-3" data-testid="server-action-config">
            <div className="space-y-1">
              <Label>Action Type</Label>
              <select
                data-testid="action-type-select"
                className="border-input bg-background ring-offset-background flex h-9 w-full rounded-md border px-3 py-1 text-sm"
                value={(getConfig().actionType as string) ?? ''}
                onChange={(e) => updateConfig({ actionType: e.target.value })}
              >
                <option value="">Select action type...</option>
                <option value="ciMergePoll">CI Merge Poll</option>
                <option value="notify">Notify</option>
                <option value="webhook">Webhook</option>
              </select>
            </div>
            {(getConfig().actionType as string) === 'ciMergePoll' && (
              <div className="space-y-1">
                <Label htmlFor="poll-interval">Poll Interval (seconds)</Label>
                <Input
                  id="poll-interval"
                  data-testid="poll-interval-input"
                  type="number"
                  value={(getConfig().pollIntervalSeconds as number) ?? 60}
                  onChange={(e) =>
                    updateConfig({
                      pollIntervalSeconds: parseInt(e.target.value) || 60,
                    })
                  }
                />
              </div>
            )}
          </div>
        )}

        {/* OnSuccess Transition */}
        <div className="space-y-1">
          <Label>On Success</Label>
          <select
            data-testid="on-success-select"
            className="border-input bg-background ring-offset-background flex h-9 w-full rounded-md border px-3 py-1 text-sm"
            value={step.onSuccess?.type ?? 'nextStep'}
            onChange={(e) =>
              update({
                onSuccess: { type: e.target.value as 'nextStep' | 'exit' | 'goToStep' },
              })
            }
          >
            {SUCCESS_TRANSITIONS.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
          {step.onSuccess?.type === 'goToStep' && (
            <select
              data-testid="on-success-target-select"
              className="border-input bg-background ring-offset-background mt-1 flex h-9 w-full rounded-md border px-3 py-1 text-sm"
              value={step.onSuccess?.targetStepId ?? ''}
              onChange={(e) =>
                update({
                  onSuccess: { type: 'goToStep', targetStepId: e.target.value },
                })
              }
            >
              <option value="">Select step...</option>
              {otherSteps.map((s) => (
                <option key={s.id} value={s.id ?? ''}>
                  {s.name}
                </option>
              ))}
            </select>
          )}
        </div>

        {/* OnFailure Transition */}
        <div className="space-y-1">
          <Label>On Failure</Label>
          <select
            data-testid="on-failure-select"
            className="border-input bg-background ring-offset-background flex h-9 w-full rounded-md border px-3 py-1 text-sm"
            value={step.onFailure?.type ?? 'exit'}
            onChange={(e) =>
              update({
                onFailure: { type: e.target.value as 'retry' | 'exit' | 'goToStep' },
              })
            }
          >
            {FAILURE_TRANSITIONS.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
          {step.onFailure?.type === 'goToStep' && (
            <select
              data-testid="on-failure-target-select"
              className="border-input bg-background ring-offset-background mt-1 flex h-9 w-full rounded-md border px-3 py-1 text-sm"
              value={step.onFailure?.targetStepId ?? ''}
              onChange={(e) =>
                update({
                  onFailure: { type: 'goToStep', targetStepId: e.target.value },
                })
              }
            >
              <option value="">Select step...</option>
              {otherSteps.map((s) => (
                <option key={s.id} value={s.id ?? ''}>
                  {s.name}
                </option>
              ))}
            </select>
          )}
        </div>

        {/* Retry Config (shown when failure is retry) */}
        {step.onFailure?.type === 'retry' && (
          <div className="flex gap-3">
            <div className="flex-1 space-y-1">
              <Label htmlFor="max-retries">Max Retries</Label>
              <Input
                id="max-retries"
                data-testid="max-retries-input"
                type="number"
                min={0}
                value={step.maxRetries ?? 0}
                onChange={(e) => update({ maxRetries: parseInt(e.target.value) || 0 })}
              />
            </div>
            <div className="flex-1 space-y-1">
              <Label htmlFor="retry-delay">Retry Delay (s)</Label>
              <Input
                id="retry-delay"
                data-testid="retry-delay-input"
                type="number"
                min={0}
                value={step.retryDelaySeconds ?? 30}
                onChange={(e) => update({ retryDelaySeconds: parseInt(e.target.value) || 30 })}
              />
            </div>
          </div>
        )}

        {/* Condition */}
        <div className="space-y-1">
          <Label htmlFor="condition">Condition (optional)</Label>
          <Input
            id="condition"
            data-testid="condition-input"
            placeholder="Skip logic expression"
            value={step.condition ?? ''}
            onChange={(e) => update({ condition: e.target.value || null })}
          />
        </div>
      </CardContent>
    </Card>
  )
}
