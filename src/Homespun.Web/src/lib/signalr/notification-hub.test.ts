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
      onIssueChanged: vi.fn(),
    }

    registerNotificationHubEvents(mockConnection, handlers)

    expect(mockConnection.on).toHaveBeenCalledWith('NotificationAdded', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('NotificationDismissed', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('IssueChanged', expect.any(Function))
  })

  it('registers branch ID generation events', () => {
    const handlers: NotificationHubEvents = {
      onBranchIdGenerated: vi.fn(),
      onBranchIdGenerationFailed: vi.fn(),
    }

    registerNotificationHubEvents(mockConnection, handlers)

    expect(mockConnection.on).toHaveBeenCalledWith('BranchIdGenerated', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('BranchIdGenerationFailed', expect.any(Function))
  })

  it('calls onBranchIdGenerated with correct parameters', () => {
    const onBranchIdGenerated = vi.fn()
    const handlers: NotificationHubEvents = { onBranchIdGenerated }

    registerNotificationHubEvents(mockConnection, handlers)

    mockConnection.simulateEvent(
      'BranchIdGenerated',
      'issue-1',
      'project-1',
      'feature/my-branch',
      true
    )

    expect(onBranchIdGenerated).toHaveBeenCalledWith(
      'issue-1',
      'project-1',
      'feature/my-branch',
      true
    )
  })

  it('calls onBranchIdGenerationFailed with correct parameters', () => {
    const onBranchIdGenerationFailed = vi.fn()
    const handlers: NotificationHubEvents = { onBranchIdGenerationFailed }

    registerNotificationHubEvents(mockConnection, handlers)

    mockConnection.simulateEvent(
      'BranchIdGenerationFailed',
      'issue-1',
      'project-1',
      'AI generation failed'
    )

    expect(onBranchIdGenerationFailed).toHaveBeenCalledWith(
      'issue-1',
      'project-1',
      'AI generation failed'
    )
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

  it('calls onIssueChanged with correct parameters', () => {
    const onIssueChanged = vi.fn()
    const handlers: NotificationHubEvents = { onIssueChanged }

    registerNotificationHubEvents(mockConnection, handlers)

    mockConnection.simulateEvent('IssueChanged', 'project-1', 'created', 'issue-1', null)

    expect(onIssueChanged).toHaveBeenCalledWith('project-1', 'created', 'issue-1', null)
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
