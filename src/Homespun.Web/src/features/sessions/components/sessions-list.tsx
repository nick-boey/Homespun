import { useState, useMemo, useEffect, useCallback } from 'react'
import { Link } from '@tanstack/react-router'
import { AlertCircle, RefreshCw, Square, Terminal } from 'lucide-react'
import { useSessions, useStopSession, sessionsQueryKey } from '../hooks/use-sessions'
import { SessionsEmptyState } from './sessions-empty-state'
import { SessionRowSkeleton } from './session-row-skeleton'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
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
import type { ClaudeSessionStatus } from '@/api/generated'
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

function getStatusLabel(status: ClaudeSessionStatus | undefined): string {
  switch (status) {
    case SessionStatus.Starting:
      return 'Starting'
    case SessionStatus.RunningHooks:
      return 'Running Hooks'
    case SessionStatus.Running:
      return 'Running'
    case SessionStatus.WaitingForInput:
      return 'Waiting'
    case SessionStatus.WaitingForQuestionAnswer:
      return 'Question'
    case SessionStatus.WaitingForPlanExecution:
      return 'Plan Ready'
    case SessionStatus.Stopped:
      return 'Stopped'
    case SessionStatus.Error:
      return 'Error'
    default:
      return 'Unknown'
  }
}

function getStatusVariant(
  status: ClaudeSessionStatus | undefined
): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (status) {
    case SessionStatus.Running:
    case SessionStatus.RunningHooks:
      return 'default'
    case SessionStatus.Starting:
    case SessionStatus.WaitingForInput:
    case SessionStatus.WaitingForQuestionAnswer:
    case SessionStatus.WaitingForPlanExecution:
      return 'secondary'
    case SessionStatus.Error:
      return 'destructive'
    case SessionStatus.Stopped:
    default:
      return 'outline'
  }
}

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

function formatRelativeTime(dateString: string | undefined): string {
  if (!dateString) return 'Unknown'

  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSecs = Math.floor(diffMs / 1000)
  const diffMins = Math.floor(diffSecs / 60)
  const diffHours = Math.floor(diffMins / 60)
  const diffDays = Math.floor(diffHours / 24)

  if (diffDays > 0) {
    return `${diffDays} day${diffDays > 1 ? 's' : ''} ago`
  }
  if (diffHours > 0) {
    return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`
  }
  if (diffMins > 0) {
    return `${diffMins} minute${diffMins > 1 ? 's' : ''} ago`
  }
  return 'just now'
}

function getModeLabel(mode: number): string {
  return mode === 0 ? 'Plan' : 'Build'
}

export function SessionsList() {
  const { data: sessions, isLoading, isError, refetch } = useSessions()
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
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-[200px]">Session</TableHead>
                <TableHead>Entity</TableHead>
                <TableHead>Mode</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Messages</TableHead>
                <TableHead>Last Activity</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {[1, 2, 3].map((i) => (
                <SessionRowSkeleton key={i} />
              ))}
            </TableBody>
          </Table>
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
              <SessionsTable
                sessions={filteredSessions}
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
              <SessionsTable sessions={filteredSessions} />
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

interface SessionsTableProps {
  sessions: Array<{
    id: string | null
    entityId: string | null
    projectId: string | null
    model: string | null
    mode: number
    status?: ClaudeSessionStatus
    createdAt?: string
    lastActivityAt?: string
    messageCount?: number
    totalCostUsd?: number
    containerId?: string | null
    containerName?: string | null
  }>
  onStopSession?: (sessionId: string) => void
  isStopPending?: boolean
}

function SessionsTable({ sessions, onStopSession, isStopPending }: SessionsTableProps) {
  return (
    <div className="rounded-md border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-[200px]">Session</TableHead>
            <TableHead>Entity</TableHead>
            <TableHead>Mode</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Messages</TableHead>
            <TableHead>Last Activity</TableHead>
            <TableHead className="text-right">Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {sessions.map((session) => (
            <TableRow key={session.id}>
              <TableCell className="font-mono text-sm">
                <Link
                  to="/sessions/$sessionId"
                  params={{ sessionId: session.id ?? '' }}
                  className="hover:underline"
                  aria-label={session.id ?? ''}
                >
                  {session.id}
                </Link>
              </TableCell>
              <TableCell>
                <span className="font-mono text-sm">{session.entityId}</span>
              </TableCell>
              <TableCell>
                <Badge variant="outline">{getModeLabel(session.mode)}</Badge>
              </TableCell>
              <TableCell>
                <Badge variant={getStatusVariant(session.status)}>
                  {getStatusLabel(session.status)}
                </Badge>
              </TableCell>
              <TableCell>{session.messageCount ?? 0}</TableCell>
              <TableCell className="text-muted-foreground text-sm">
                {formatRelativeTime(session.lastActivityAt)}
              </TableCell>
              <TableCell className="text-right">
                {isActiveStatus(session.status) && onStopSession && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => session.id && onStopSession(session.id)}
                    disabled={isStopPending}
                    aria-label="Stop"
                  >
                    <Square className="mr-1 h-3 w-3" />
                    Stop
                  </Button>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
