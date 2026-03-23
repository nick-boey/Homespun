import { Moon, Sun } from 'lucide-react'
import { useTheme } from 'next-themes'
import { cn } from '@/lib/utils'

export function ThemeToggle() {
  const { theme, setTheme } = useTheme()

  return (
    <button
      onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
      className={cn(
        'inline-flex h-9 w-9 items-center justify-center rounded-md',
        'text-muted-foreground hover:bg-accent hover:text-accent-foreground',
        'transition-colors'
      )}
      aria-label="Toggle theme"
    >
      <Sun className="h-4 w-4 scale-100 dark:scale-0" />
      <Moon className="absolute h-4 w-4 scale-0 dark:scale-100" />
    </button>
  )
}
