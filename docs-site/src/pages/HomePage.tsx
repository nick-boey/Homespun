import { Link } from 'react-router-dom'
import { BookOpen, Wrench, Users, AlertTriangle } from 'lucide-react'
import { cn } from '@/lib/utils'

const cards = [
  {
    title: 'Installation',
    description: 'Get Homespun up and running with Docker in minutes.',
    slug: 'installation',
    icon: BookOpen,
  },
  {
    title: 'Usage Guide',
    description: 'Learn about projects, issues, agents, and PR workflows.',
    slug: 'usage',
    icon: Wrench,
  },
  {
    title: 'Multi-User Setup',
    description: 'Configure multiple user instances for team collaboration.',
    slug: 'multi-user',
    icon: Users,
  },
  {
    title: 'Troubleshooting',
    description: 'Resolve common issues with setup, agents, and GitHub integration.',
    slug: 'troubleshooting',
    icon: AlertTriangle,
  },
]

export function HomePage() {
  return (
    <div data-testid="home-page">
      <div className="mb-8">
        <h1 className="text-foreground text-3xl font-bold tracking-tight">
          Homespun Documentation
        </h1>
        <p className="text-muted-foreground mt-2 text-lg">
          Manage development features and AI agents with project management, Git clone integration,
          GitHub PR synchronization, and Claude Code agent orchestration.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        {cards.map((card) => (
          <Link
            key={card.slug}
            to={`/docs/${card.slug}`}
            className={cn(
              'group border-border bg-card rounded-lg border p-6',
              'hover:border-foreground/20 hover:bg-accent/50 transition-colors'
            )}
          >
            <div className="flex items-center gap-3">
              <card.icon className="text-muted-foreground group-hover:text-foreground h-5 w-5" />
              <h2 className="text-foreground text-lg font-semibold">{card.title}</h2>
            </div>
            <p className="text-muted-foreground mt-2 text-sm">{card.description}</p>
          </Link>
        ))}
      </div>
    </div>
  )
}
