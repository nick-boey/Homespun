import { useMemo } from 'react'
import { SkillCategory } from '@/api'
import type { SkillDescriptor } from '@/api/generated/types.gen'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Loader } from '@/components/ui/loader'
import { useProjectSkills } from '../hooks/use-project-skills'
import { SkillArgInput } from './skill-arg-input'

export const NONE_SKILL_VALUE = '__none__'

export interface SkillPickerProps {
  projectId: string
  category: SkillCategory
  selectedSkillName: string | null | undefined
  onSkillChange: (name: string | null) => void
  argValues: Record<string, string>
  onArgValuesChange: (values: Record<string, string>) => void
  disabled?: boolean
  noneLabel?: string
}

/**
 * Picker for dispatch skills. Fetches skills for the project via
 * `useProjectSkills`, filters to the requested category, and renders a
 * Select plus dynamic inputs for the selected skill's args.
 */
export function SkillPicker({
  projectId,
  category,
  selectedSkillName,
  onSkillChange,
  argValues,
  onArgValuesChange,
  disabled,
  noneLabel = 'None — free-text only',
}: SkillPickerProps) {
  const { data, isLoading } = useProjectSkills(projectId)

  const skills = useMemo<SkillDescriptor[]>(
    () => skillsForCategory(data, category),
    [data, category]
  )

  const selectedSkill = useMemo(
    () => skills.find((s) => s.name === selectedSkillName) ?? null,
    [skills, selectedSkillName]
  )

  const selectValue = selectedSkillName ?? NONE_SKILL_VALUE

  const handleValueChange = (value: string) => {
    onSkillChange(value === NONE_SKILL_VALUE ? null : value)
  }

  const handleArgChange = (name: string, value: string) => {
    onArgValuesChange({ ...argValues, [name]: value })
  }

  if (isLoading) {
    return (
      <div className="flex items-center gap-2">
        <Loader variant="circular" size="sm" />
        <span className="text-muted-foreground text-sm">Loading skills…</span>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <Select value={selectValue} onValueChange={handleValueChange} disabled={disabled}>
        <SelectTrigger className="w-56" aria-label="Select skill">
          <SelectValue placeholder="Select skill" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={NONE_SKILL_VALUE}>{noneLabel}</SelectItem>
          {skills.map((skill) => (
            <SelectItem key={skill.name ?? ''} value={skill.name ?? ''}>
              {skill.name}
              {skill.mode ? ` (${skill.mode})` : ''}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      {selectedSkill?.description ? (
        <p className="text-muted-foreground text-xs">{selectedSkill.description}</p>
      ) : null}

      {selectedSkill?.args?.length ? (
        <div className="flex flex-col gap-3">
          {selectedSkill.args.map((arg) => (
            <SkillArgInput
              key={arg.name ?? ''}
              arg={arg}
              value={argValues[arg.name ?? ''] ?? ''}
              onChange={(v) => handleArgChange(arg.name ?? '', v)}
              disabled={disabled}
            />
          ))}
        </div>
      ) : null}
    </div>
  )
}

function skillsForCategory(
  data:
    | {
        openSpec?: SkillDescriptor[] | null
        homespun?: SkillDescriptor[] | null
        general?: SkillDescriptor[] | null
      }
    | undefined,
  category: SkillCategory
): SkillDescriptor[] {
  if (!data) return []
  switch (category) {
    case SkillCategory.OPEN_SPEC:
      return data.openSpec ?? []
    case SkillCategory.HOMESPUN:
      return data.homespun ?? []
    case SkillCategory.GENERAL:
      return data.general ?? []
    default:
      return []
  }
}
