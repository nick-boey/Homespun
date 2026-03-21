import { createFileRoute } from '@tanstack/react-router'
import { PromptsList } from '@/features/prompts'

export const Route = createFileRoute('/prompts')({
  component: GlobalPromptsPage,
})

export default function GlobalPromptsPage() {
  return (
    <div className="container mx-auto space-y-8 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Global Prompts</h1>
        <p className="text-muted-foreground">Manage prompts available to all projects</p>
      </div>

      {/* Agent Prompts Section - includes Issue Agent Prompts when isGlobal=true */}
      <PromptsList isGlobal />
    </div>
  )
}
