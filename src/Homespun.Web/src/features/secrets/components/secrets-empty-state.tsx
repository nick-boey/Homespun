import { Key } from 'lucide-react'

export interface SecretsEmptyStateProps {
  title?: string
  description?: string
}

export function SecretsEmptyState({
  title = 'No secrets configured',
  description = 'Add environment variables to securely configure your project.',
}: SecretsEmptyStateProps) {
  return (
    <div className="border-border flex flex-col items-center justify-center rounded-lg border border-dashed p-8 text-center">
      <Key className="text-muted-foreground mb-4 h-12 w-12" />
      <h3 className="text-lg font-medium">{title}</h3>
      <p className="text-muted-foreground mt-1 text-sm">{description}</p>
    </div>
  )
}
