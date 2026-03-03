import { Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { Home } from 'lucide-react'

export function NotFound() {
  return (
    <div className="flex min-h-[400px] flex-col items-center justify-center gap-6 p-8">
      <div className="text-center">
        <h1 className="text-muted-foreground text-6xl font-bold">404</h1>
        <h2 className="text-foreground mt-2 text-2xl font-semibold">Page not found</h2>
        <p className="text-muted-foreground mt-2">
          Sorry, we couldn't find the page you're looking for.
        </p>
      </div>
      <Button asChild>
        <Link to="/">
          <Home className="h-4 w-4" />
          Back to home
        </Link>
      </Button>
    </div>
  )
}
