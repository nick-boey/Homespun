import { MessageSquarePlus } from 'lucide-react'

export interface PromptsEmptyStateProps {
  title?: string
  description?: string
}

export function PromptsEmptyState({
  title = 'No prompts yet',
  description = 'Create custom system prompts to customize how agents work on this project.',
}: PromptsEmptyStateProps) {
  return (
    <div className="border-border flex flex-col items-center justify-center rounded-lg border border-dashed p-8 text-center">
      <MessageSquarePlus className="text-muted-foreground h-12 w-12" />
      <h3 className="mt-4 text-lg font-semibold">{title}</h3>
      <p className="text-muted-foreground mt-1 max-w-sm text-sm">{description}</p>
    </div>
  )
}
