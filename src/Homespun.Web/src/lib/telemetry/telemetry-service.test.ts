import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { TelemetryService } from './telemetry-service'
import { TelemetryEventType, type ClientTelemetryBatch } from '@/api/generated'

// Mock session manager
vi.mock('./session-manager', () => ({
  getSessionId: vi.fn().mockReturnValue('test-session-id'),
}))

// Mock fetch
const mockFetch = vi.fn()
globalThis.fetch = mockFetch as typeof fetch

describe('TelemetryService', () => {
  let service: TelemetryService

  beforeEach(() => {
    vi.clearAllMocks()
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({}),
    })
  })

  afterEach(() => {
    if (service) {
      service.stop()
    }
  })

  describe('configuration', () => {
    it('respects enabled config', () => {
      service = new TelemetryService({ enabled: false })

      // Track an event - should not call fetch
      service.trackEvent('test', { foo: 'bar' })

      // Force flush
      service.flush()

      // Should not have called fetch since telemetry is disabled
      expect(mockFetch).not.toHaveBeenCalled()
    })
  })

  describe('trackPageView', () => {
    it('tracks page view and sends on flush', async () => {
      service = new TelemetryService({ enabled: true })

      service.trackPageView('/dashboard', 'Dashboard')

      await service.flush()

      expect(mockFetch).toHaveBeenCalledWith('/api/client-telemetry', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: expect.any(String),
      })

      const body = JSON.parse(mockFetch.mock.calls[0][1].body) as ClientTelemetryBatch
      expect(body.sessionId).toBe('test-session-id')
      expect(body.events).toHaveLength(1)
      expect(body.events![0]).toMatchObject({
        type: TelemetryEventType.PAGE_VIEW,
        name: '/dashboard',
        properties: {
          title: 'Dashboard',
        },
      })
    })

    it('includes additional properties', async () => {
      service = new TelemetryService({ enabled: true })

      service.trackPageView('/project/123', 'Project Details', {
        projectId: '123',
        source: 'navigation',
      })

      await service.flush()

      const body = JSON.parse(mockFetch.mock.calls[0][1].body) as ClientTelemetryBatch
      expect(body.events![0].properties).toEqual({
        title: 'Project Details',
        projectId: '123',
        source: 'navigation',
      })
    })
  })

  describe('trackEvent', () => {
    it('tracks custom event', async () => {
      service = new TelemetryService({ enabled: true })

      service.trackEvent('button_click', {
        buttonId: 'create-project',
        location: 'header',
      })

      await service.flush()

      const body = JSON.parse(mockFetch.mock.calls[0][1].body) as ClientTelemetryBatch
      expect(body.events![0]).toMatchObject({
        type: TelemetryEventType.EVENT,
        name: 'button_click',
        properties: {
          buttonId: 'create-project',
          location: 'header',
        },
      })
    })
  })

  describe('trackException', () => {
    it('tracks exception with error object', async () => {
      service = new TelemetryService({ enabled: true })

      const error = new Error('Test error')
      error.stack = 'Error: Test error\n    at test.js:10:5'

      service.trackException(error, {
        component: 'ProjectList',
        severity: 'error',
      })

      // trackException calls flush immediately, so we don't need to flush manually
      await vi.waitFor(() => expect(mockFetch).toHaveBeenCalled())

      const body = JSON.parse(mockFetch.mock.calls[0][1].body) as ClientTelemetryBatch
      expect(body.events![0]).toMatchObject({
        type: TelemetryEventType.EXCEPTION,
        name: 'Error: Test error',
        properties: {
          message: 'Test error',
          stack: 'Error: Test error\n    at test.js:10:5',
          component: 'ProjectList',
          severity: 'error',
        },
      })
    })
  })

  describe('trackDependency', () => {
    it('tracks successful dependency', async () => {
      service = new TelemetryService({ enabled: true })

      service.trackDependency('GET /api/projects', 1234, true, 200, {
        method: 'GET',
        path: '/api/projects',
      })

      await service.flush()

      const body = JSON.parse(mockFetch.mock.calls[0][1].body) as ClientTelemetryBatch
      expect(body.events![0]).toMatchObject({
        type: TelemetryEventType.DEPENDENCY,
        name: 'GET /api/projects',
        durationMs: 1234,
        success: true,
        statusCode: 200,
        properties: {
          method: 'GET',
          path: '/api/projects',
        },
      })
    })
  })

  describe('batch sending', () => {
    it('handles non-ok response', async () => {
      service = new TelemetryService({ enabled: true })

      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 500,
        statusText: 'Internal Server Error',
      })

      service.trackEvent('test')

      // Flush should complete (batcher will handle the error internally)
      await service.flush()

      // Verify the request was attempted
      expect(mockFetch).toHaveBeenCalled()
    })
  })

  describe('timestamps', () => {
    it('generates ISO timestamp', async () => {
      service = new TelemetryService({ enabled: true })

      const before = new Date().toISOString()
      service.trackEvent('test')
      const after = new Date().toISOString()

      await service.flush()

      const body = JSON.parse(mockFetch.mock.calls[0][1].body) as ClientTelemetryBatch
      const timestamp = body.events![0].timestamp!

      expect(new Date(timestamp).toISOString()).toBe(timestamp)
      expect(timestamp >= before).toBe(true)
      expect(timestamp <= after).toBe(true)
    })
  })
})
