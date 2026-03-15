/**
 * Tests for Notification hub event handlers and methods.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { HubConnection } from '@microsoft/signalr'
import {
  registerNotificationHubEvents,
  createNotificationHubMethods,
  type NotificationHubEvents,
} from './notification-hub'
import type { NotificationDto } from '@/types/signalr'

function createMockConnection(): HubConnection & {
  _handlers: Map<string, (...args: unknown[]) => void>
  simulateEvent: (name: string, ...args: unknown[]) => void
} {
  const handlers = new Map<string, (...args: unknown[]) => void>()

  return {
    on: vi.fn((name: string, handler: (...args: unknown[]) => void) => {
      handlers.set(name, handler)
    }),
    off: vi.fn((name: string) => {
      handlers.delete(name)
    }),
    invoke: vi.fn().mockResolvedValue(undefined),
    _handlers: handlers,
    simulateEvent: (name: string, ...args: unknown[]) => {
      const handler = handlers.get(name)
      if (handler) {
        handler(...args)
      }
    },
  } as unknown as HubConnection & {
    _handlers: Map<string, (...args: unknown[]) => void>
    simulateEvent: (name: string, ...args: unknown[]) => void
  }
}

describe('registerNotificationHubEvents', () => {
  let mockConnection: ReturnType<typeof createMockConnection>

  beforeEach(() => {
    mockConnection = createMockConnection()
  })

  it('registers notification events', () => {
    const handlers: NotificationHubEvents = {
      onNotificationAdded: vi.fn(),
      onNotificationDismissed: vi.fn(),
      onIssuesChanged: vi.fn(),
    }

    registerNotificationHubEvents(mockConnection, handlers)

    expect(mockConnection.on).toHaveBeenCalledWith('NotificationAdded', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('NotificationDismissed', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('IssuesChanged', expect.any(Function))
  })

  it('calls onNotificationAdded when notification is received', () => {
    const onNotificationAdded = vi.fn()
    const handlers: NotificationHubEvents = { onNotificationAdded }

    registerNotificationHubEvents(mockConnection, handlers)

    const mockNotification: NotificationDto = {
      id: 'notif-1',
      type: 'info',
      title: 'Test',
      message: 'Test message',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    }

    mockConnection.simulateEvent('NotificationAdded', mockNotification)

    expect(onNotificationAdded).toHaveBeenCalledWith(mockNotification)
  })

  it('calls onIssuesChanged with correct parameters', () => {
    const onIssuesChanged = vi.fn()
    const handlers: NotificationHubEvents = { onIssuesChanged }

    registerNotificationHubEvents(mockConnection, handlers)

    mockConnection.simulateEvent('IssuesChanged', 'project-1', 'Created', 'issue-1')

    expect(onIssuesChanged).toHaveBeenCalledWith('project-1', 'Created', 'issue-1')
  })

  it('returns cleanup function that removes handlers', () => {
    const handlers: NotificationHubEvents = {
      onNotificationAdded: vi.fn(),
      onNotificationDismissed: vi.fn(),
    }

    const cleanup = registerNotificationHubEvents(mockConnection, handlers)

    expect(mockConnection._handlers.size).toBe(2)

    cleanup()

    expect(mockConnection.off).toHaveBeenCalledWith('NotificationAdded', expect.any(Function))
    expect(mockConnection.off).toHaveBeenCalledWith('NotificationDismissed', expect.any(Function))
  })
})

describe('createNotificationHubMethods', () => {
  let mockConnection: ReturnType<typeof createMockConnection>

  beforeEach(() => {
    mockConnection = createMockConnection()
  })

  it('creates joinProjectGroup method', async () => {
    const methods = createNotificationHubMethods(mockConnection)

    await methods.joinProjectGroup('project-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinProjectGroup', 'project-1')
  })

  it('creates leaveProjectGroup method', async () => {
    const methods = createNotificationHubMethods(mockConnection)

    await methods.leaveProjectGroup('project-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith('LeaveProjectGroup', 'project-1')
  })

  it('creates getActiveNotifications method with projectId', async () => {
    const mockNotifications: NotificationDto[] = []
    mockConnection.invoke = vi.fn().mockResolvedValue(mockNotifications)

    const methods = createNotificationHubMethods(mockConnection)
    const result = await methods.getActiveNotifications('project-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith('GetActiveNotifications', 'project-1')
    expect(result).toBe(mockNotifications)
  })

  it('creates getActiveNotifications method without projectId', async () => {
    const mockNotifications: NotificationDto[] = []
    mockConnection.invoke = vi.fn().mockResolvedValue(mockNotifications)

    const methods = createNotificationHubMethods(mockConnection)
    const result = await methods.getActiveNotifications()

    expect(mockConnection.invoke).toHaveBeenCalledWith('GetActiveNotifications', null)
    expect(result).toBe(mockNotifications)
  })

  it('creates dismissNotification method', async () => {
    const methods = createNotificationHubMethods(mockConnection)

    await methods.dismissNotification('notif-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith('DismissNotification', 'notif-1')
  })
})
