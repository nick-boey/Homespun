import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <div className="flex flex-col items-center justify-center py-20">
      <h1 className="text-foreground text-4xl font-bold">404</h1>
      <p className="text-muted-foreground mt-2">Page not found.</p>
      <Link to="/" className="text-primary hover:text-primary/80 mt-4 text-sm underline">
        Back to home
      </Link>
    </div>
  )
}
