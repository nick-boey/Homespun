import { useState, useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Eye, Code } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Markdown } from '@/components/ui/markdown'
import { SessionMode } from '@/api'
import type { AgentPrompt } from '@/api'

const promptSchema = z.object({
  name: z.string().min(1, 'Prompt name is required'),
  initialMessage: z.string().optional(),
  mode: z.enum([SessionMode.PLAN, SessionMode.BUILD]),
})

type PromptFormData = z.infer<typeof promptSchema>

export interface PromptFormProps {
  prompt?: AgentPrompt | null
  onSubmit: (data: PromptFormData) => Promise<void>
  onCancel: () => void
  isSubmitting?: boolean
  /** Hide the name field (for editing fixed prompts like Issue Agent prompts) */
  hideNameField?: boolean
}

export function PromptForm({
  prompt,
  onSubmit,
  onCancel,
  isSubmitting,
  hideNameField,
}: PromptFormProps) {
  const [showPreview, setShowPreview] = useState(false)

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors },
    reset,
  } = useForm<PromptFormData>({
    resolver: zodResolver(promptSchema),
    defaultValues: {
      name: prompt?.name ?? '',
      initialMessage: prompt?.initialMessage ?? '',
      mode: prompt?.mode ?? SessionMode.BUILD,
    },
  })

  useEffect(() => {
    if (prompt) {
      reset({
        name: prompt.name ?? '',
        initialMessage: prompt.initialMessage ?? '',
        mode: prompt.mode ?? SessionMode.BUILD,
      })
    }
  }, [prompt, reset])

  const initialMessage = watch('initialMessage')
  const mode = watch('mode')

  const handleFormSubmit = handleSubmit(async (data) => {
    await onSubmit(data)
  })

  return (
    <form onSubmit={handleFormSubmit} className="space-y-6">
      {!hideNameField && (
        <div className="space-y-2">
          <Label htmlFor="name">Name</Label>
          <Input id="name" placeholder="My Custom Prompt" {...register('name')} />
          {errors.name && <p className="text-destructive text-sm">{errors.name.message}</p>}
        </div>
      )}

      <div className="space-y-2">
        <Label htmlFor="mode">Mode</Label>
        <Select value={mode} onValueChange={(value) => setValue('mode', value as SessionMode)}>
          <SelectTrigger>
            <SelectValue placeholder="Select mode" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SessionMode.BUILD}>Build (Full access)</SelectItem>
            <SelectItem value={SessionMode.PLAN}>Plan (Read-only)</SelectItem>
          </SelectContent>
        </Select>
        <p className="text-muted-foreground text-xs">
          {mode === SessionMode.PLAN
            ? 'Plan mode is read-only and cannot modify files.'
            : 'Build mode has full access to create and modify files.'}
        </p>
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <Label htmlFor="initialMessage">System Prompt</Label>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => setShowPreview(!showPreview)}
          >
            {showPreview ? (
              <>
                <Code className="mr-1 h-4 w-4" />
                Edit
              </>
            ) : (
              <>
                <Eye className="mr-1 h-4 w-4" />
                Preview
              </>
            )}
          </Button>
        </div>

        {showPreview ? (
          <div className="border-input min-h-[200px] rounded-md border p-4">
            {initialMessage ? (
              <Markdown className="prose prose-sm dark:prose-invert max-w-none">
                {initialMessage}
              </Markdown>
            ) : (
              <p className="text-muted-foreground italic">No content to preview</p>
            )}
          </div>
        ) : (
          <Textarea
            id="initialMessage"
            placeholder="Enter the system prompt that will be used to configure the agent..."
            className="min-h-[200px] font-mono text-sm"
            {...register('initialMessage')}
          />
        )}
        <p className="text-muted-foreground text-xs">
          Markdown is supported. This prompt will be passed to the agent as the system message.
        </p>
      </div>

      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </Button>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Saving...' : prompt ? 'Update Prompt' : 'Create Prompt'}
        </Button>
      </div>
    </form>
  )
}
