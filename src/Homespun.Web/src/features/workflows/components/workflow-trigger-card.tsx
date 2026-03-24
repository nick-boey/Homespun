import type {
  WorkflowTrigger,
  WorkflowEventType as WorkflowEventTypeValue,
} from '@/api/generated/types.gen'
import { WorkflowEventType, WorkflowTriggerType } from '@/api/generated/types.gen'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { Switch } from '@/components/ui/switch'
import { Checkbox } from '@/components/ui/checkbox'

interface WorkflowTriggerCardProps {
  trigger: WorkflowTrigger | undefined
  onChange: (trigger: WorkflowTrigger) => void
}

const TRIGGER_TYPES = [
  { value: WorkflowTriggerType.MANUAL, label: 'Manual' },
  { value: WorkflowTriggerType.EVENT, label: 'Event' },
  { value: WorkflowTriggerType.SCHEDULED, label: 'Scheduled' },
  { value: WorkflowTriggerType.WEBHOOK, label: 'Webhook' },
] as const

const EVENT_TYPE_LABELS: Record<WorkflowEventTypeValue, string> = {
  [WorkflowEventType.ISSUE_CREATED]: 'Issue Created',
  [WorkflowEventType.ISSUE_STATUS_CHANGED]: 'Issue Status Changed',
  [WorkflowEventType.ISSUE_ASSIGNED]: 'Issue Assigned',
  [WorkflowEventType.PULL_REQUEST_OPENED]: 'Pull Request Opened',
  [WorkflowEventType.PULL_REQUEST_MERGED]: 'Pull Request Merged',
  [WorkflowEventType.PULL_REQUEST_REVIEW_REQUESTED]: 'Pull Request Review Requested',
  [WorkflowEventType.PULL_REQUEST_CHECKS_COMPLETED]: 'Pull Request Checks Completed',
  [WorkflowEventType.AGENT_SESSION_COMPLETED]: 'Agent Session Completed',
  [WorkflowEventType.AGENT_SESSION_FAILED]: 'Agent Session Failed',
  [WorkflowEventType.BRANCH_CREATED]: 'Branch Created',
  [WorkflowEventType.BRANCH_MERGED]: 'Branch Merged',
  [WorkflowEventType.CUSTOM]: 'Custom',
}

const DEFAULT_TRIGGER: WorkflowTrigger = { type: 'manual', enabled: true }

export function WorkflowTriggerCard({ trigger, onChange }: WorkflowTriggerCardProps) {
  const current = trigger ?? DEFAULT_TRIGGER

  function handleTypeChange(type: WorkflowTrigger['type']) {
    const base = { type, enabled: current.enabled }
    switch (type) {
      case 'event':
        onChange({ ...base, eventConfig: { eventTypes: [] } })
        break
      case 'scheduled':
        onChange({
          ...base,
          scheduleConfig: { cronExpression: '', timezone: 'UTC', skipIfRunning: false },
        })
        break
      case 'webhook':
        onChange({
          ...base,
          webhookConfig: { secret: '', contentType: 'application/json' },
        })
        break
      default:
        onChange(base)
    }
  }

  function toggleEventType(eventType: WorkflowEventTypeValue) {
    const existing = current.eventConfig?.eventTypes ?? []
    const updated = existing.includes(eventType)
      ? existing.filter((e) => e !== eventType)
      : [...existing, eventType]
    onChange({ ...current, eventConfig: { ...current.eventConfig, eventTypes: updated } })
  }

  return (
    <Card data-testid="workflow-trigger-card">
      <CardHeader>
        <CardTitle className="text-base">Trigger Configuration</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Trigger Type */}
        <div className="space-y-1">
          <Label>Trigger Type</Label>
          <div className="flex gap-1">
            {TRIGGER_TYPES.map((t) => (
              <Button
                key={t.value}
                data-testid={`trigger-type-${t.value}`}
                variant={current.type === t.value ? 'default' : 'outline'}
                size="sm"
                onClick={() => handleTypeChange(t.value)}
              >
                {t.label}
              </Button>
            ))}
          </div>
        </div>

        {/* Event Config */}
        {current.type === 'event' && (
          <div className="space-y-3" data-testid="event-config">
            <Label>Event Types</Label>
            <div className="grid grid-cols-2 gap-2">
              {Object.entries(EVENT_TYPE_LABELS).map(([value, label]) => (
                <div key={value} className="flex items-center gap-2">
                  <Checkbox
                    data-testid={`event-type-${value}`}
                    checked={(current.eventConfig?.eventTypes ?? []).includes(
                      value as WorkflowEventTypeValue
                    )}
                    onCheckedChange={() => toggleEventType(value as WorkflowEventTypeValue)}
                  />
                  <Label className="text-sm font-normal">{label}</Label>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Schedule Config */}
        {current.type === 'scheduled' && (
          <div className="space-y-3" data-testid="schedule-config">
            <div className="space-y-1">
              <Label htmlFor="cron-expression">Cron Expression</Label>
              <Input
                id="cron-expression"
                data-testid="cron-expression-input"
                placeholder="0 */6 * * *"
                value={current.scheduleConfig?.cronExpression ?? ''}
                onChange={(e) =>
                  onChange({
                    ...current,
                    scheduleConfig: { ...current.scheduleConfig, cronExpression: e.target.value },
                  })
                }
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="timezone">Timezone</Label>
              <Input
                id="timezone"
                data-testid="timezone-input"
                placeholder="UTC"
                value={current.scheduleConfig?.timezone ?? ''}
                onChange={(e) =>
                  onChange({
                    ...current,
                    scheduleConfig: { ...current.scheduleConfig!, timezone: e.target.value },
                  })
                }
              />
            </div>
            <div className="flex items-center gap-2">
              <Switch
                data-testid="skip-if-running-switch"
                checked={current.scheduleConfig?.skipIfRunning ?? false}
                onCheckedChange={(checked) =>
                  onChange({
                    ...current,
                    scheduleConfig: {
                      ...current.scheduleConfig!,
                      skipIfRunning: checked === true,
                    },
                  })
                }
              />
              <Label className="text-sm font-normal">Skip if already running</Label>
            </div>
          </div>
        )}

        {/* Webhook Config */}
        {current.type === 'webhook' && (
          <div className="space-y-3" data-testid="webhook-config">
            <div className="space-y-1">
              <Label htmlFor="webhook-secret">Secret</Label>
              <Input
                id="webhook-secret"
                data-testid="webhook-secret-input"
                type="password"
                value={current.webhookConfig?.secret ?? ''}
                onChange={(e) =>
                  onChange({
                    ...current,
                    webhookConfig: { ...current.webhookConfig, secret: e.target.value },
                  })
                }
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="webhook-content-type">Content Type</Label>
              <Input
                id="webhook-content-type"
                data-testid="webhook-content-type-input"
                value={current.webhookConfig?.contentType ?? 'application/json'}
                onChange={(e) =>
                  onChange({
                    ...current,
                    webhookConfig: { ...current.webhookConfig, contentType: e.target.value },
                  })
                }
              />
            </div>
          </div>
        )}

        {/* Trigger Enabled Toggle */}
        <div className="flex items-center gap-2">
          <Switch
            data-testid="trigger-enabled-switch"
            checked={current.enabled ?? true}
            onCheckedChange={(checked) => onChange({ ...current, enabled: checked === true })}
          />
          <Label className="text-sm font-normal">Trigger enabled</Label>
        </div>
      </CardContent>
    </Card>
  )
}
