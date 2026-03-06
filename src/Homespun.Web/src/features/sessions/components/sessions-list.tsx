import { useState, useMemo, useEffect, useCallback } from 'react'
import { AlertCircle, RefreshCw, Terminal } from 'lucide-react'
import { useEnrichedSessions } from '../hooks/use-enriched-sessions'
import { useStopSession, sessionsQueryKey } from '../hooks/use-sessions'
import { SessionCard } from './session-card'
import { SessionsEmptyState } from './sessions-empty-state'
import { SessionCardSkeleton } from './session-card-skeleton'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { registerClaudeCodeHubEvents } from '@/lib/signalr/claude-code-hub'
import type { ClaudeSessionStatus } from '@/api/generated/types.gen'
import { useQueryClient } from '@tanstack/react-query'

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
  const { sessions, isLoading, isError, refetch } = useEnrichedSessions()
  const stopSession = useStopSession()
  const queryClient = useQueryClient()
  const { connection, isConnected } = useClaudeCodeHub()

  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [activeTab, setActiveTab] = useState<'active' | 'archived'>('active')
  const [sessionToStop, setSessionToStop] = useState<string | null>(null)

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

  const handleStopSession = useCallback(
    (sessionId: string) => {
      setSessionToStop(sessionId)
    },
    [setSessionToStop]
  )

  const confirmStopSession = useCallback(() => {
    if (sessionToStop) {
      stopSession.mutate(sessionToStop)
      setSessionToStop(null)
    }
  }, [sessionToStop, stopSession])

  const cancelStopSession = useCallback(() => {
    setSessionToStop(null)
  }, [])

  const filteredSessions = useMemo(() => {
    if (!sessions) return []

    let filtered = sessions

    // Filter by active/archived tab
    if (activeTab === 'active') {
      filtered = filtered.filter(
        (s) =>
          s.session.status !== SessionStatus.Stopped && s.session.status !== SessionStatus.Error
      )
    } else {
      filtered = filtered.filter(
        (s) =>
          s.session.status === SessionStatus.Stopped || s.session.status === SessionStatus.Error
      )
    }

    // Filter by status dropdown
    if (statusFilter !== 'all') {
      filtered = filtered.filter((enrichedSession) => {
        const status = enrichedSession.session.status
        if (statusFilter === 'active') {
          return isActiveStatus(status)
        }
        if (statusFilter === 'stopped') {
          return status === SessionStatus.Stopped
        }
        if (statusFilter === 'error') {
          return status === SessionStatus.Error
        }
        return true
      })
    }

    // Sort by last activity (most recent first)
    return filtered.sort((a, b) => {
      const dateA = a.session.lastActivityAt ? new Date(a.session.lastActivityAt).getTime() : 0
      const dateB = b.session.lastActivityAt ? new Date(b.session.lastActivityAt).getTime() : 0
      return dateB - dateA
    })
  }, [sessions, statusFilter, activeTab])

  // Group filtered sessions by project
  const groupedFilteredSessions = useMemo(() => {
    const groups = new Map<string, typeof filteredSessions>()

    filteredSessions.forEach((enrichedSession) => {
      const projectId = enrichedSession.session.projectId || 'no-project'
      const existing = groups.get(projectId) || []
      groups.set(projectId, [...existing, enrichedSession])
    })

    // Sort groups by project name
    return Array.from(groups.entries()).sort(([, sessionsA], [, sessionsB]) => {
      const nameA = sessionsA[0]?.projectName || 'Unknown Project'
      const nameB = sessionsB[0]?.projectName || 'Unknown Project'
      return nameA.localeCompare(nameB)
    })
  }, [filteredSessions])

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
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
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

  const activeSessions = sessions.filter(
    (s) => s.session.status !== SessionStatus.Stopped && s.session.status !== SessionStatus.Error
  )
  const archivedSessions = sessions.filter(
    (s) => s.session.status === SessionStatus.Stopped || s.session.status === SessionStatus.Error
  )

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
              <TabsTrigger value="active">Active ({activeSessions.length})</TabsTrigger>
              <TabsTrigger value="archived">Archived ({archivedSessions.length})</TabsTrigger>
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
              <SessionsGrid
                groupedSessions={groupedFilteredSessions}
                onStopSession={handleStopSession}
                isStopPending={stopSession.isPending}
              />
            )}
          </TabsContent>

          <TabsContent value="archived" className="mt-4">
            {filteredSessions.length === 0 ? (
              <div className="text-muted-foreground flex flex-col items-center justify-center rounded-lg border border-dashed p-8 text-center">
                <Terminal className="text-muted-foreground/50 h-12 w-12" />
                <p className="mt-4">No archived sessions</p>
              </div>
            ) : (
              <SessionsGrid groupedSessions={groupedFilteredSessions} />
            )}
          </TabsContent>
        </Tabs>
      </div>

      <AlertDialog
        open={sessionToStop !== null}
        onOpenChange={(open) => !open && cancelStopSession()}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Stop Session</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to stop this session? This will terminate the running agent.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel onClick={cancelStopSession}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={confirmStopSession}>Stop Session</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}

interface SessionsGridProps {
  groupedSessions: Array<[string, Array<ReturnType<typeof useEnrichedSessions>['sessions'][0]>]>
  onStopSession?: (sessionId: string) => void
  isStopPending?: boolean
}

function SessionsGrid({ groupedSessions, onStopSession, isStopPending }: SessionsGridProps) {
  return (
    <div className="space-y-6">
      {groupedSessions.map(([projectId, projectSessions]) => {
        const projectName = projectSessions[0]?.projectName || 'Unknown Project'
        return (
          <div key={projectId}>
            <h3 className="text-muted-foreground mb-3 text-sm font-medium">
              {projectName}
              <span className="text-muted-foreground ml-2 text-xs">
                ({projectSessions.length} session{projectSessions.length !== 1 ? 's' : ''})
              </span>
            </h3>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
              {projectSessions.map((enrichedSession) => (
                <SessionCard
                  key={enrichedSession.session.id}
                  session={enrichedSession.session}
                  entityTitle={enrichedSession.entityTitle}
                  entityType={enrichedSession.entityType}
                  projectName={enrichedSession.projectName}
                  messageCount={enrichedSession.messageCount}
                  onStop={onStopSession}
                  isStopPending={isStopPending}
                />
              ))}
            </div>
          </div>
        )
      })}
    </div>
  )
}
