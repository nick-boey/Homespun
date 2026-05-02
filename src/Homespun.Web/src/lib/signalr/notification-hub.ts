/**
 * Notification Hub event handler types and registration.
 */

import type { HubConnection } from '@microsoft/signalr'
import type { IssueResponse } from '@/api'
import type { NotificationDto, IssueChangeKind } from '@/types/signalr'

// ============================================================================
// Event Handler Types
// ============================================================================

export interface NotificationHubEvents {
  onNotificationAdded?: (notification: NotificationDto) => void
  onNotificationDismissed?: (notificationId: string) => void
  /**
   * Unified per-issue mutation event. The server emits `IssueChanged` for
   * every create / update / delete (and topology change). `issue` is the
   * canonical post-mutation body for `created` / `updated` and `null` for
   * `deleted`. `issueId` is `null` for bulk events (fleece-sync,
   * clone lifecycle) — clients treat a null id as "invalidate every issue
   * cache for this project".
   */
  onIssueChanged?: (
    projectId: string,
    kind: IssueChangeKind,
    issueId: string | null,
    issue: IssueResponse | null
  ) => void
  onBranchIdGenerated?: (
    issueId: string,
    projectId: string,
    branchId: string,
    wasAiGenerated: boolean
  ) => void
  onBranchIdGenerationFailed?: (issueId: string, projectId: string, error: string) => void
}

// ============================================================================
// Event Registration
// ============================================================================

/**
 * Registers event handlers for the Notification hub.
 * Returns a cleanup function to unregister all handlers.
 */
export function registerNotificationHubEvents(
  connection: HubConnection,
  handlers: NotificationHubEvents
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

  register('NotificationAdded', handlers.onNotificationAdded)
  register('NotificationDismissed', handlers.onNotificationDismissed)
  register('IssueChanged', handlers.onIssueChanged)
  register('BranchIdGenerated', handlers.onBranchIdGenerated)
  register('BranchIdGenerationFailed', handlers.onBranchIdGenerationFailed)

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

export interface NotificationHubMethods {
  joinProjectGroup(projectId: string): Promise<void>
  leaveProjectGroup(projectId: string): Promise<void>
  getActiveNotifications(projectId?: string): Promise<NotificationDto[]>
  dismissNotification(notificationId: string): Promise<void>
}

/**
 * Creates typed methods for invoking Notification hub server methods.
 */
export function createNotificationHubMethods(connection: HubConnection): NotificationHubMethods {
  return {
    joinProjectGroup: (projectId: string) => connection.invoke('JoinProjectGroup', projectId),
    leaveProjectGroup: (projectId: string) => connection.invoke('LeaveProjectGroup', projectId),
    getActiveNotifications: (projectId?: string) =>
      connection.invoke<NotificationDto[]>('GetActiveNotifications', projectId ?? null),
    dismissNotification: (notificationId: string) =>
      connection.invoke('DismissNotification', notificationId),
  }
}
