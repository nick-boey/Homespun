import { createFileRoute } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/')({
  component: Index,
})

function Index() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 p-4">
      <h1 className="text-4xl font-bold">Homespun</h1>
      <p className="text-muted-foreground">Welcome to Homespun Web</p>
      <Button>Get Started</Button>
    </div>
  )
}
