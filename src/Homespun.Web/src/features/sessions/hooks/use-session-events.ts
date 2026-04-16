/**
 * Unified hook for rendering a Claude Code session from AG-UI envelopes.
 *
 * This hook is the client-side counterpart to the server's `SessionEventIngestor` +
 * replay endpoint. It:
 *
 *  1. Subscribes to the SignalR `ReceiveSessionEvent` method for live envelopes.
 *  2. On mount and on SignalR reconnect, fetches `GET /api/sessions/{id}/events?since=N`
 *     where `N` is the last seq the client already applied (0 on first mount).
 *  3. Feeds both live and replay envelopes through the same `applyEnvelope` reducer so
 *     the final render state is identical regardless of arrival path.
 *  4. Deduplicates envelopes by `eventId` with a bounded LRU (default 10k entries per
 *     session), protecting the reducer from `?mode=full` replays and the rare race where
 *     a live and replay envelope carry the same event.
 *  5. Persists `lastSeenSeq` in the Zustand session-events store so unmount/remount
 *     within a single browser session doesn't replay the entire history again.
 *
 * The hook does not own component rendering; consumers select state slices from the
 * returned object (messages, pendingPlan, pendingQuestion, etc.) and render from there.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { useSessionEventsStore } from '@/stores/session-events-store'
import type { SessionEventEnvelope } from '@/types/session-events'
import { client } from '@/api'
import {
  applyEnvelope,
  initialAGUISessionState,
  type AGUISessionState,
} from '@/features/sessions/utils/agui-reducer'

/** Maximum number of recent eventIds remembered for dedup per session. */
const DEDUP_CAPACITY = 10_000

export interface UseSessionEventsResult {
  state: AGUISessionState
  isReplayingHistory: boolean
  replayError: Error | null
}

/**
 * Bounded FIFO for recent eventIds — constant-time lookups, O(1) amortized adds, and
 * automatic eviction when full.
 */
class EventIdDedupSet {
  private readonly seen = new Set<string>()
  private readonly order: string[] = []
  private readonly capacity: number

  constructor(capacity: number) {
    this.capacity = capacity
  }

  has(id: string): boolean {
    return this.seen.has(id)
  }

  add(id: string): void {
    if (this.seen.has(id)) return
    this.seen.add(id)
    this.order.push(id)
    if (this.order.length > this.capacity) {
      const evict = this.order.shift()
      if (evict) this.seen.delete(evict)
    }
  }

  clear(): void {
    this.seen.clear()
    this.order.length = 0
  }
}

export function useSessionEvents(sessionId: string | undefined | null): UseSessionEventsResult {
  const { connection, isConnected } = useClaudeCodeHub()
  const setStoredState = useSessionEventsStore((s) => s.setState)
  const storedState = useSessionEventsStore((s) => (sessionId ? s.sessions[sessionId] : undefined))

  // Local copy for reactive consumers — mirrors the stored state but survives store
  // reactivity glitches (the store is the source of truth for persistence across
  // unmount/remount; the local ref is the source of truth for in-flight application).
  const [renderState, setRenderState] = useState<AGUISessionState>(
    storedState ?? initialAGUISessionState
  )

  const stateRef = useRef<AGUISessionState>(renderState)
  const dedupRef = useRef<EventIdDedupSet>(new EventIdDedupSet(DEDUP_CAPACITY))
  const replayBufferRef = useRef<SessionEventEnvelope[]>([])
  const isReplayingRef = useRef<boolean>(false)
  const [isReplayingHistory, setIsReplayingHistory] = useState<boolean>(false)
  const [replayError, setReplayError] = useState<Error | null>(null)

  // Apply an envelope: dedup by eventId, then fold through the reducer. If a replay is in
  // flight, buffer live envelopes so we can apply them strictly after the replay batch to
  // preserve reducer invariants.
  const applyOne = useCallback(
    (envelope: SessionEventEnvelope) => {
      if (!sessionId || envelope.sessionId !== sessionId) return
      // A single A2A event can translate to multiple AG-UI envelopes (e.g. an agent text
      // message produces start/content/end), all sharing parent `eventId`. Key dedup on
      // (eventId, event.type) so we drop true duplicates without dropping siblings.
      const dedupKey = `${envelope.eventId}::${envelope.event.type}`
      if (dedupRef.current.has(dedupKey)) return
      if (isReplayingRef.current) {
        replayBufferRef.current.push(envelope)
        return
      }
      dedupRef.current.add(dedupKey)
      const next = applyEnvelope(stateRef.current, envelope)
      stateRef.current = next
      setRenderState(next)
      setStoredState(sessionId, next)
    },
    [sessionId, setStoredState]
  )

  // Session id change → reset local tracking. The store retains the prior session's state
  // under its own key so navigating back restores quickly.
  useEffect(() => {
    if (!sessionId) {
      stateRef.current = initialAGUISessionState
      setRenderState(initialAGUISessionState)
      dedupRef.current.clear()
      replayBufferRef.current = []
      return
    }
    const current = useSessionEventsStore.getState().getState(sessionId)
    stateRef.current = current
    setRenderState(current)
    dedupRef.current.clear()
    replayBufferRef.current = []
  }, [sessionId])

  // Live subscription to ReceiveSessionEvent.
  useEffect(() => {
    if (!connection || !sessionId) return

    const handler = (deliveredSessionId: string, envelope: SessionEventEnvelope) => {
      // The hub addresses the group by sessionId, but double-check so a leaked subscription
      // on a different sessionId never pollutes state.
      if (deliveredSessionId !== sessionId) return
      applyOne(envelope)
    }

    connection.on('ReceiveSessionEvent', handler)
    return () => {
      connection.off('ReceiveSessionEvent', handler)
    }
  }, [connection, sessionId, applyOne])

  // Replay fetch on mount and on SignalR reconnect. `isConnected` flips to false during
  // reconnect and back to true after reconnecting — the second transition triggers the
  // catch-up fetch using the current lastSeenSeq.
  useEffect(() => {
    if (!sessionId) return
    if (!isConnected) return

    let cancelled = false

    const runReplay = async () => {
      isReplayingRef.current = true
      setIsReplayingHistory(true)
      setReplayError(null)

      const since = stateRef.current.lastSeenSeq
      const url = `${client.getConfig().baseUrl ?? ''}/api/sessions/${encodeURIComponent(sessionId)}/events?since=${since}`

      try {
        const response = await fetch(url, { credentials: 'include' })
        if (cancelled) return

        if (response.status === 404) {
          // Session has no event log yet — nothing to replay. Not an error.
          return
        }
        if (!response.ok) {
          throw new Error(`Replay fetch failed: HTTP ${response.status}`)
        }

        const envelopes = (await response.json()) as SessionEventEnvelope[]
        if (cancelled) return

        // Flush replay envelopes through the reducer first, then drain any live envelopes
        // that arrived during the fetch. Dedup keeps the merge correct even if the live
        // burst and the replay response overlap.
        isReplayingRef.current = false
        for (const envelope of envelopes) {
          applyOne(envelope)
        }
        const buffered = replayBufferRef.current
        replayBufferRef.current = []
        for (const envelope of buffered) {
          applyOne(envelope)
        }
      } catch (err) {
        if (!cancelled) {
          setReplayError(err instanceof Error ? err : new Error(String(err)))
        }
      } finally {
        if (!cancelled) {
          isReplayingRef.current = false
          setIsReplayingHistory(false)
        }
      }
    }

    void runReplay()

    return () => {
      cancelled = true
      isReplayingRef.current = false
    }
  }, [sessionId, isConnected, applyOne])

  return {
    state: renderState,
    isReplayingHistory,
    replayError,
  }
}
