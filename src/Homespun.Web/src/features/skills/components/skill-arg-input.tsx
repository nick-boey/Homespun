import { SkillArgKind } from '@/api'
import type { SkillArgDescriptor } from '@/api/generated/types.gen'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export interface SkillArgInputProps {
  arg: SkillArgDescriptor
  value: string
  onChange: (value: string) => void
  disabled?: boolean
}

/**
 * Renders a single input for a skill arg. The input kind determines the
 * placeholder and any lightweight hinting. All kinds currently render as
 * text inputs — richer pickers (issue, change, phase-list) are tracked as
 * a follow-up.
 */
export function SkillArgInput({ arg, value, onChange, disabled }: SkillArgInputProps) {
  const name = arg.name ?? ''
  const label = arg.label?.trim() ? arg.label : name
  const placeholder = placeholderFor(arg.kind)
  const inputId = `skill-arg-${name}`

  return (
    <div className="space-y-1.5">
      <Label htmlFor={inputId} className="text-sm">
        {label}
      </Label>
      <Input
        id={inputId}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
      />
      {arg.description?.trim() ? (
        <p className="text-muted-foreground text-xs">{arg.description}</p>
      ) : null}
    </div>
  )
}

function placeholderFor(kind: SkillArgDescriptor['kind']): string {
  switch (kind) {
    case SkillArgKind.ISSUE:
      return 'Issue ID (e.g. ABC123)'
    case SkillArgKind.CHANGE:
      return 'Change name'
    case SkillArgKind.PHASE_LIST:
      return 'Phases (comma-separated)'
    case SkillArgKind.FREE_TEXT:
    default:
      return ''
  }
}
