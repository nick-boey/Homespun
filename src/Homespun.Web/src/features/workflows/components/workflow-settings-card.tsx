import { useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Button } from '@/components/ui/button'
import type { WorkflowDefinition, UpdateWorkflowRequest } from '@/api/generated/types.gen'

interface WorkflowSettingsCardProps {
  workflow: WorkflowDefinition
  projectId: string
  onSave: (updates: Partial<UpdateWorkflowRequest>) => void
  isSaving: boolean
}

export function WorkflowSettingsCard({ workflow, onSave, isSaving }: WorkflowSettingsCardProps) {
  const [title, setTitle] = useState(workflow.title ?? '')
  const [description, setDescription] = useState(workflow.description ?? '')
  const [defaultTimeoutSeconds, setDefaultTimeoutSeconds] = useState(
    workflow.settings?.defaultTimeoutSeconds ?? 3600
  )
  const [continueOnFailure, setContinueOnFailure] = useState(
    workflow.settings?.continueOnFailure ?? false
  )
  const [enabled, setEnabled] = useState(workflow.enabled ?? false)

  function handleSave() {
    const updates: Partial<UpdateWorkflowRequest> = {}

    if (title !== (workflow.title ?? '')) {
      updates.title = title
    }
    if (description !== (workflow.description ?? '')) {
      updates.description = description
    }
    if (enabled !== (workflow.enabled ?? false)) {
      updates.enabled = enabled
    }

    const origTimeout = workflow.settings?.defaultTimeoutSeconds ?? 3600
    const origContinue = workflow.settings?.continueOnFailure ?? false
    if (defaultTimeoutSeconds !== origTimeout || continueOnFailure !== origContinue) {
      updates.settings = {
        defaultTimeoutSeconds,
        continueOnFailure,
      }
    }

    if (Object.keys(updates).length === 0) return

    onSave(updates)
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Settings</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="settings-title">Title</Label>
          <Input
            id="settings-title"
            data-testid="settings-title-input"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="settings-description">Description</Label>
          <Textarea
            id="settings-description"
            data-testid="settings-description-input"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="settings-timeout">Default Timeout (seconds)</Label>
          <Input
            id="settings-timeout"
            data-testid="settings-timeout-input"
            type="number"
            value={defaultTimeoutSeconds}
            onChange={(e) => setDefaultTimeoutSeconds(Number(e.target.value))}
          />
        </div>

        <div className="flex items-center justify-between">
          <Label htmlFor="settings-continue-on-failure">Continue on Failure</Label>
          <Switch
            id="settings-continue-on-failure"
            data-testid="settings-continue-on-failure-switch"
            checked={continueOnFailure}
            onCheckedChange={setContinueOnFailure}
          />
        </div>

        <div className="flex items-center justify-between">
          <Label htmlFor="settings-enabled">Enabled</Label>
          <Switch
            id="settings-enabled"
            data-testid="settings-enabled-switch"
            checked={enabled}
            onCheckedChange={setEnabled}
          />
        </div>

        <Button data-testid="settings-save-button" onClick={handleSave} disabled={isSaving}>
          {isSaving ? 'Saving…' : 'Save Settings'}
        </Button>
      </CardContent>
    </Card>
  )
}
