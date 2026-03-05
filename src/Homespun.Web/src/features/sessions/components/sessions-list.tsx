import { useState, useMemo, useEffect, useCallback } from 'react'
import { AlertCircle, RefreshCw, Terminal } from 'lucide-react'
import { useSessions, sessionsQueryKey } from '../hooks/use-sessions'
import { SessionsEmptyState } from './sessions-empty-state'
import { SessionCard } from './session-card'
import { SessionCardSkeleton } from './session-card-skeleton'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { registerClaudeCodeHubEvents } from '@/lib/signalr/claude-code-hub'
import { useQueryClient } from '@tanstack/react-query'
import type { ClaudeSessionStatus } from '@/api/generated'

// Status enum values from backend
const SessionStatus = {
  Starting: 0,
  RunningHooks: 1,
  Running: 2,
  WaitingForInput: 3,
  WaitingForQuestionAnswer: 4,
  WaitingForPlanExecution: 5,
  Stopped: 6,
  Error: 7,
} as const

type StatusFilter = 'all' | 'active' | 'stopped' | 'error'

function isActiveStatus(status: ClaudeSessionStatus | undefined): boolean {
  return (
    status === SessionStatus.Starting ||
    status === SessionStatus.RunningHooks ||
    status === SessionStatus.Running ||
    status === SessionStatus.WaitingForInput ||
    status === SessionStatus.WaitingForQuestionAnswer ||
    status === SessionStatus.WaitingForPlanExecution
  )
}

export function SessionsList() {
  const { data: sessions, isLoading, isError, refetch } = useSessions()
  const queryClient = useQueryClient()
  const { connection, isConnected } = useClaudeCodeHub()

  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [activeTab, setActiveTab] = useState<'active' | 'archived'>('active')

  // Subscribe to SignalR events for real-time updates
  useEffect(() => {
    if (!connection || !isConnected) return

    const cleanup = registerClaudeCodeHubEvents(connection, {
      onSessionStarted: () => {
        queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
      },
      onSessionStopped: () => {
        queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
      },
      onSessionStatusChanged: () => {
        queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
      },
      onSessionError: () => {
        queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
      },
    })

    return cleanup
  }, [connection, isConnected, queryClient])

  const filteredSessions = useMemo(() => {
    if (!sessions) return []

    let filtered = sessions

    // Filter by active/archived tab
    if (activeTab === 'active') {
      filtered = filtered.filter(
        (s) => s.status !== SessionStatus.Stopped && s.status !== SessionStatus.Error
      )
    } else {
      filtered = filtered.filter(
        (s) => s.status === SessionStatus.Stopped || s.status === SessionStatus.Error
      )
    }

    // Filter by status dropdown
    if (statusFilter !== 'all') {
      filtered = filtered.filter((session) => {
        if (statusFilter === 'active') {
          return isActiveStatus(session.status)
        }
        if (statusFilter === 'stopped') {
          return session.status === SessionStatus.Stopped
        }
        if (statusFilter === 'error') {
          return session.status === SessionStatus.Error
        }
        return true
      })
    }

    // Sort by last activity (most recent first)
    return filtered.sort((a, b) => {
      const dateA = a.lastActivityAt ? new Date(a.lastActivityAt).getTime() : 0
      const dateB = b.lastActivityAt ? new Date(b.lastActivityAt).getTime() : 0
      return dateB - dateA
    })
  }, [sessions, statusFilter, activeTab])

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <Tabs value="active" className="w-full">
            <TabsList>
              <TabsTrigger value="active">Active</TabsTrigger>
              <TabsTrigger value="archived">Archived</TabsTrigger>
            </TabsList>
          </Tabs>
        </div>
        <div className="grid gap-4 grid-cols-1 md:grid-cols-2 lg:grid-cols-3">
          {[1, 2, 3, 4, 5, 6].map((i) => (
            <SessionCardSkeleton key={i} />
          ))}
        </div>
      </div>
    )
  }

  if (isError) {
    return (
      <div className="border-destructive/50 bg-destructive/10 flex flex-col items-center justify-center rounded-lg border p-8 text-center">
        <AlertCircle className="text-destructive h-10 w-10" />
        <h3 className="mt-4 text-lg font-semibold">Error loading sessions</h3>
        <p className="text-muted-foreground mt-2 text-sm">
          Something went wrong while loading your sessions.
        </p>
        <Button variant="outline" className="mt-4" onClick={() => refetch()}>
          <RefreshCw className="mr-2 h-4 w-4" />
          Retry
        </Button>
      </div>
    )
  }

  if (!sessions || sessions.length === 0) {
    return <SessionsEmptyState />
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-4">
        <Tabs
          value={activeTab}
          onValueChange={(value) => setActiveTab(value as 'active' | 'archived')}
          className="w-full"
        >
          <div className="flex items-center justify-between">
            <TabsList>
              <TabsTrigger value="active">
                Active (
                {
                  sessions.filter(
                    (s) => s.status !== SessionStatus.Stopped && s.status !== SessionStatus.Error
                  ).length
                }
                )
              </TabsTrigger>
              <TabsTrigger value="archived">
                Archived (
                {
                  sessions.filter(
                    (s) => s.status === SessionStatus.Stopped || s.status === SessionStatus.Error
                  ).length
                }
                )
              </TabsTrigger>
            </TabsList>
            <div className="flex items-center gap-2">
              <Select
                value={statusFilter}
                onValueChange={(value) => setStatusFilter(value as StatusFilter)}
              >
                <SelectTrigger className="w-[180px]" aria-label="Filter by status">
                  <SelectValue placeholder="Filter by status" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All Statuses</SelectItem>
                  <SelectItem value="active">Active</SelectItem>
                  <SelectItem value="stopped">Stopped</SelectItem>
                  <SelectItem value="error">Error</SelectItem>
                </SelectContent>
              </Select>
              <Button variant="outline" size="icon" onClick={() => refetch()}>
                <RefreshCw className="h-4 w-4" />
              </Button>
            </div>
          </div>

          <TabsContent value="active" className="mt-4">
            {filteredSessions.length === 0 ? (
              <div className="text-muted-foreground flex flex-col items-center justify-center rounded-lg border border-dashed p-8 text-center">
                <Terminal className="text-muted-foreground/50 h-12 w-12" />
                <p className="mt-4">No active sessions</p>
              </div>
            ) : (
              <div className="grid gap-4 grid-cols-1 md:grid-cols-2 lg:grid-cols-3">
                {filteredSessions.map((session) => (
                  <SessionCard key={session.id} session={session} />
                ))}
              </div>
            )}
          </TabsContent>

          <TabsContent value="archived" className="mt-4">
            {filteredSessions.length === 0 ? (
              <div className="text-muted-foreground flex flex-col items-center justify-center rounded-lg border border-dashed p-8 text-center">
                <Terminal className="text-muted-foreground/50 h-12 w-12" />
                <p className="mt-4">No archived sessions</p>
              </div>
            ) : (
              <div className="grid gap-4 grid-cols-1 md:grid-cols-2 lg:grid-cols-3">
                {filteredSessions.map((session) => (
                  <SessionCard key={session.id} session={session} />
                ))}
              </div>
            )}
          </TabsContent>
        </Tabs>
      </div>
    </div>
  )
}