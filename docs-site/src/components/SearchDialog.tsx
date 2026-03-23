import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, X } from 'lucide-react'
import { type SearchResult, initSearchIndex, search } from '@/lib/search'
import { cn } from '@/lib/utils'

export function SearchDialog() {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResult[]>([])
  const [selectedIndex, setSelectedIndex] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const navigate = useNavigate()

  useEffect(() => {
    initSearchIndex()
  }, [])

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault()
        setOpen(true)
      }
    }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [])

  useEffect(() => {
    if (open) {
      inputRef.current?.focus()
    }
  }, [open])

  const handleOpen = () => {
    setQuery('')
    setResults([])
    setSelectedIndex(0)
    setOpen(true)
  }

  const handleClose = () => {
    setOpen(false)
  }

  const handleSearch = useCallback((value: string) => {
    setQuery(value)
    setSelectedIndex(0)
    if (value.trim()) {
      setResults(search(value))
    } else {
      setResults([])
    }
  }, [])

  const handleSelect = useCallback(
    (result: SearchResult) => {
      navigate(`/docs/${result.page.slug}`)
      setOpen(false)
    },
    [navigate]
  )

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setSelectedIndex((i) => Math.min(i + 1, results.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setSelectedIndex((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter' && results[selectedIndex]) {
      handleSelect(results[selectedIndex])
    } else if (e.key === 'Escape') {
      handleClose()
    }
  }

  if (!open) {
    return (
      <button
        onClick={handleOpen}
        className={cn(
          'border-input bg-background flex h-9 items-center gap-2 rounded-md border px-3',
          'text-muted-foreground hover:bg-accent text-sm',
          'w-full sm:w-64'
        )}
      >
        <Search className="h-4 w-4" />
        <span className="flex-1 text-left">Search docs...</span>
        <kbd className="border-border bg-muted hidden rounded border px-1.5 text-xs sm:inline">
          Ctrl+K
        </kbd>
      </button>
    )
  }

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center pt-[20vh]">
      <div className="fixed inset-0 bg-black/50" onClick={handleClose} />
      <div className="border-border bg-background relative z-10 w-full max-w-lg rounded-lg border shadow-lg">
        <div className="border-border flex items-center border-b px-3">
          <Search className="text-muted-foreground h-4 w-4" />
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => handleSearch(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Search documentation..."
            className="placeholder:text-muted-foreground flex-1 bg-transparent px-3 py-3 text-sm outline-none"
          />
          <button onClick={handleClose} className="text-muted-foreground hover:text-foreground">
            <X className="h-4 w-4" />
          </button>
        </div>
        {results.length > 0 && (
          <ul className="max-h-80 overflow-y-auto p-2">
            {results.map((result, i) => (
              <li key={result.page.slug}>
                <button
                  onClick={() => handleSelect(result)}
                  className={cn(
                    'w-full rounded-md px-3 py-2 text-left text-sm',
                    i === selectedIndex ? 'bg-accent text-accent-foreground' : 'hover:bg-accent/50'
                  )}
                >
                  <div className="font-medium">{result.page.title}</div>
                  <div className="text-muted-foreground mt-0.5 line-clamp-1 text-xs">
                    {result.excerpt}
                  </div>
                </button>
              </li>
            ))}
          </ul>
        )}
        {query && results.length === 0 && (
          <div className="text-muted-foreground px-3 py-6 text-center text-sm">
            No results found.
          </div>
        )}
      </div>
    </div>
  )
}
