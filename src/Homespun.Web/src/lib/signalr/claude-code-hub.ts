/**
 * Claude Code Hub event handler types and registration.
 */

import type { HubConnection } from '@microsoft/signalr'
import type {
  ClaudeSession,
  ClaudeSessionStatus,
  SessionMode,
  RunStartedEvent,
  RunFinishedEvent,
  RunErrorEvent,
  TextMessageStartEvent,
  TextMessageContentEvent,
  TextMessageEndEvent,
  ToolCallStartEvent,
  ToolCallArgsEvent,
  ToolCallEndEvent,
  ToolCallResultEvent,
  StateSnapshotEvent,
  StateDeltaEvent,
  CustomEvent,
} from '@/types/signalr'

// ============================================================================
// Event Handler Types
// ============================================================================

export interface ClaudeCodeHubEvents {
  // Session lifecycle events
  onSessionStarted?: (session: ClaudeSession) => void
  onSessionStopped?: (sessionId: string) => void
  onSessionState?: (session: ClaudeSession) => void
  onSessionStatusChanged?: (
    sessionId: string,
    status: ClaudeSessionStatus,
    hasPendingPlanApproval: boolean
  ) => void
  onSessionModeModelChanged?: (sessionId: string, mode: SessionMode, model: string) => void
  onSessionResultReceived?: (sessionId: string, totalCostUsd: number, durationMs: number) => void
  onContextCleared?: (sessionId: string) => void
  onSessionContextCleared?: (oldSessionId: string, newSession: ClaudeSession) => void
  onSessionError?: (
    sessionId: string,
    errorMessage: string,
    errorSubtype: string | null,
    isRecoverable: boolean
  ) => void
  onSessionContainerRestarting?: (sessionId: string) => void
  onSessionContainerRestarted?: (sessionId: string, session: ClaudeSession) => void

  // AG-UI Events
  onAGUIRunStarted?: (evt: RunStartedEvent) => void
  onAGUIRunFinished?: (evt: RunFinishedEvent) => void
  onAGUIRunError?: (evt: RunErrorEvent) => void
  onAGUITextMessageStart?: (evt: TextMessageStartEvent) => void
  onAGUITextMessageContent?: (evt: TextMessageContentEvent) => void
  onAGUITextMessageEnd?: (evt: TextMessageEndEvent) => void
  onAGUIToolCallStart?: (evt: ToolCallStartEvent) => void
  onAGUIToolCallArgs?: (evt: ToolCallArgsEvent) => void
  onAGUIToolCallEnd?: (evt: ToolCallEndEvent) => void
  onAGUIToolCallResult?: (evt: ToolCallResultEvent) => void
  onAGUIStateSnapshot?: (evt: StateSnapshotEvent) => void
  onAGUIStateDelta?: (evt: StateDeltaEvent) => void
  onAGUICustomEvent?: (evt: CustomEvent) => void
}

// ============================================================================
// Event Registration
// ============================================================================

/**
 * Registers event handlers for the Claude Code hub.
 * Returns a cleanup function to unregister all handlers.
 */
export function registerClaudeCodeHubEvents(
  connection: HubConnection,
  handlers: ClaudeCodeHubEvents
): () => void {
  const registrations: Array<{ method: string; handler: (...args: unknown[]) => void }> = []

  const register = <T extends unknown[]>(
    method: string,
    handler: ((...args: T) => void) | undefined
  ) => {
    if (handler) {
      const wrappedHandler = (...args: unknown[]) => handler(...(args as T))
      connection.on(method, wrappedHandler)
      registrations.push({ method, handler: wrappedHandler })
    }
  }

  // Session lifecycle events
  register('SessionStarted', handlers.onSessionStarted)
  register('SessionStopped', handlers.onSessionStopped)
  register('SessionState', handlers.onSessionState)
  register('SessionStatusChanged', handlers.onSessionStatusChanged)
  register('SessionModeModelChanged', handlers.onSessionModeModelChanged)
  register('SessionResultReceived', handlers.onSessionResultReceived)
  register('ContextCleared', handlers.onContextCleared)
  register('SessionContextCleared', handlers.onSessionContextCleared)
  register('SessionError', handlers.onSessionError)
  register('SessionContainerRestarting', handlers.onSessionContainerRestarting)
  register('SessionContainerRestarted', handlers.onSessionContainerRestarted)

  // AG-UI Events
  register('AGUI_RunStarted', handlers.onAGUIRunStarted)
  register('AGUI_RunFinished', handlers.onAGUIRunFinished)
  register('AGUI_RunError', handlers.onAGUIRunError)
  register('AGUI_TextMessageStart', handlers.onAGUITextMessageStart)
  register('AGUI_TextMessageContent', handlers.onAGUITextMessageContent)
  register('AGUI_TextMessageEnd', handlers.onAGUITextMessageEnd)
  register('AGUI_ToolCallStart', handlers.onAGUIToolCallStart)
  register('AGUI_ToolCallArgs', handlers.onAGUIToolCallArgs)
  register('AGUI_ToolCallEnd', handlers.onAGUIToolCallEnd)
  register('AGUI_ToolCallResult', handlers.onAGUIToolCallResult)
  register('AGUI_StateSnapshot', handlers.onAGUIStateSnapshot)
  register('AGUI_StateDelta', handlers.onAGUIStateDelta)
  register('AGUI_Custom', handlers.onAGUICustomEvent)

  // Return cleanup function
  return () => {
    for (const { method, handler } of registrations) {
      connection.off(method, handler)
    }
  }
}

// ============================================================================
// Hub Methods (Client-to-Server)
// ============================================================================

export interface ClaudeCodeHubMethods {
  joinSession(sessionId: string): Promise<void>
  leaveSession(sessionId: string): Promise<void>
  sendMessage(sessionId: string, message: string, mode?: SessionMode): Promise<void>
  stopSession(sessionId: string): Promise<void>
  interruptSession(sessionId: string): Promise<void>
  getAllSessions(): Promise<ClaudeSession[]>
  getProjectSessions(projectId: string): Promise<ClaudeSession[]>
  getSession(sessionId: string): Promise<ClaudeSession | null>
  answerQuestion(sessionId: string, answersJson: string): Promise<void>
  executePlan(sessionId: string, clearContext?: boolean): Promise<void>
  approvePlan(
    sessionId: string,
    approved: boolean,
    keepContext: boolean,
    feedback?: string | null
  ): Promise<void>
  getCachedMessageCount(sessionId: string): Promise<number>
  restartSession(sessionId: string): Promise<ClaudeSession | null>
  setSessionMode(sessionId: string, mode: SessionMode): Promise<void>
  setSessionModel(sessionId: string, model: string): Promise<void>
  clearContextAndStartNew(sessionId: string, initialPrompt?: string | null): Promise<ClaudeSession>
}

/**
 * Creates typed methods for invoking Claude Code hub server methods.
 */
export function createClaudeCodeHubMethods(connection: HubConnection): ClaudeCodeHubMethods {
  return {
    joinSession: (sessionId: string) => connection.invoke('JoinSession', sessionId),
    leaveSession: (sessionId: string) => connection.invoke('LeaveSession', sessionId),
    sendMessage: (sessionId: string, message: string, mode: SessionMode = 'build') =>
      connection.invoke('SendMessage', sessionId, message, mode),
    stopSession: (sessionId: string) => connection.invoke('StopSession', sessionId),
    interruptSession: (sessionId: string) => connection.invoke('InterruptSession', sessionId),
    getAllSessions: () => connection.invoke<ClaudeSession[]>('GetAllSessions'),
    getProjectSessions: (projectId: string) =>
      connection.invoke<ClaudeSession[]>('GetProjectSessions', projectId),
    getSession: (sessionId: string) =>
      connection.invoke<ClaudeSession | null>('GetSession', sessionId),
    answerQuestion: (sessionId: string, answersJson: string) =>
      connection.invoke('AnswerQuestion', sessionId, answersJson),
    executePlan: (sessionId: string, clearContext = true) =>
      connection.invoke('ExecutePlan', sessionId, clearContext),
    approvePlan: (
      sessionId: string,
      approved: boolean,
      keepContext: boolean,
      feedback?: string | null
    ) => connection.invoke('ApprovePlan', sessionId, approved, keepContext, feedback),
    getCachedMessageCount: (sessionId: string) =>
      connection.invoke<number>('GetCachedMessageCount', sessionId),
    restartSession: (sessionId: string) =>
      connection.invoke<ClaudeSession | null>('RestartSession', sessionId),
    setSessionMode: (sessionId: string, mode: SessionMode) =>
      connection.invoke('SetSessionMode', sessionId, mode),
    setSessionModel: (sessionId: string, model: string) =>
      connection.invoke('SetSessionModel', sessionId, model),
    clearContextAndStartNew: (sessionId: string, initialPrompt?: string | null) =>
      connection.invoke<ClaudeSession>('ClearContextAndStartNew', sessionId, initialPrompt),
  }
}
