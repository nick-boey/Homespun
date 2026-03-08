import { createFileRoute } from '@tanstack/react-router'
import { PromptsList } from '@/features/prompts'

export const Route = createFileRoute('/prompts')({
  component: GlobalPromptsPage,
})

export default function GlobalPromptsPage() {
  return (
    <div className="container mx-auto p-6">
      <div className="mb-6">
        <h1 className="text-2xl font-semibold">Global Prompts</h1>
        <p className="text-muted-foreground">Manage prompts available to all projects</p>
      </div>
      <PromptsList isGlobal />
    </div>
  )
}
