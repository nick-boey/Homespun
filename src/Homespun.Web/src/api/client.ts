/**
 * API Client Configuration
 *
 * This module configures and exports the OpenAPI-generated client with:
 * - Base URL configuration
 * - Error handling interceptors
 * - Authentication header injection (for future use)
 */

import { client } from './generated/client.gen'
import type { Config } from './generated/client'
import { getGlobalTelemetryService } from '@/lib/telemetry/telemetry-singleton'

/** API error response structure */
export interface ApiError {
  status: number
  statusText: string
  message: string
  details?: unknown
}

/** Custom error class for API errors */
export class ApiClientError extends Error {
  readonly status: number
  readonly statusText: string
  readonly details?: unknown

  constructor(status: number, statusText: string, message: string, details?: unknown) {
    super(message)
    this.name = 'ApiClientError'
    this.status = status
    this.statusText = statusText
    this.details = details
  }
}

/**
 * Configure the API client with the base URL.
 * In development, this uses the Vite proxy configured in vite.config.ts.
 * In production, this should be configured with the actual API URL.
 */
export function configureApiClient(config?: Partial<Config>): void {
  const baseUrl = config?.baseUrl ?? getDefaultBaseUrl()

  client.setConfig({
    baseUrl,
    ...config,
  })

  // Map to store request start times for duration calculation
  const requestStartTimes = new Map<string, number>()

  // Set up request interceptor to track start time
  client.interceptors.request.use((request: Request) => {
    const requestId = `${request.method} ${request.url}`
    requestStartTimes.set(requestId, Date.now())

    if (import.meta.env.DEV) {
      console.debug('[API Request]', request.method, request.url)
    }

    return request
  })

  // Set up response interceptor for successful responses
  client.interceptors.response.use((response: Response, request: Request) => {
    const requestId = `${request.method} ${request.url}`
    const startTime = requestStartTimes.get(requestId)
    requestStartTimes.delete(requestId)

    if (import.meta.env.DEV) {
      console.debug('[API Response]', request.method, request.url, response.status)
    }

    // Track dependency telemetry for successful responses
    if (startTime && !isExcludedFromTelemetry(request.url)) {
      const duration = Date.now() - startTime
      const telemetry = getGlobalTelemetryService()
      telemetry?.trackDependency(
        `${request.method} ${getPathFromUrl(request.url)}`,
        duration,
        response.ok,
        response.status,
        {
          method: request.method,
          path: getPathFromUrl(request.url),
          statusText: response.statusText,
        }
      )
    }

    return response
  })

  // Set up error handling interceptor
  client.interceptors.error.use(
    (error: unknown, response: Response | undefined, request: Request) => {
      const requestId = `${request.method} ${request.url}`
      const startTime = requestStartTimes.get(requestId)
      requestStartTimes.delete(requestId)

      if (response) {
        // HTTP error response
        const apiError: ApiError = {
          status: response.status,
          statusText: response.statusText,
          message: typeof error === 'string' ? error : getErrorMessage(error),
          details: error,
        }

        console.error('[API Error]', {
          url: request.url,
          status: response.status,
          error: apiError.message,
        })

        // Track dependency telemetry for error responses
        if (startTime && !isExcludedFromTelemetry(request.url)) {
          const duration = Date.now() - startTime
          const telemetry = getGlobalTelemetryService()
          telemetry?.trackDependency(
            `${request.method} ${getPathFromUrl(request.url)}`,
            duration,
            false,
            response.status,
            {
              method: request.method,
              path: getPathFromUrl(request.url),
              error: apiError.message,
              statusText: response.statusText,
            }
          )
        }

        return new ApiClientError(
          apiError.status,
          apiError.statusText,
          apiError.message,
          apiError.details
        )
      }

      // Network or other error (no response)
      console.error('[API Network Error]', {
        url: request.url,
        error: error instanceof Error ? error.message : String(error),
      })

      // Track dependency telemetry for network errors
      if (startTime && !isExcludedFromTelemetry(request.url)) {
        const duration = Date.now() - startTime
        const telemetry = getGlobalTelemetryService()
        telemetry?.trackDependency(
          `${request.method} ${getPathFromUrl(request.url)}`,
          duration,
          false,
          0,
          {
            method: request.method,
            path: getPathFromUrl(request.url),
            error: getErrorMessage(error),
            errorType: 'network',
          }
        )
      }

      return new ApiClientError(0, 'Network Error', getErrorMessage(error), error)
    }
  )
}

/**
 * Set the authentication token for API requests.
 * This injects the Authorization header for all subsequent requests.
 */
export function setAuthToken(token: string | null): void {
  if (token) {
    client.setConfig({
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })
  } else {
    // Remove auth header by setting empty headers
    // Note: This is a simplified approach; in a real app you might want to
    // maintain other headers while removing just the Authorization header
    client.setConfig({
      headers: {},
    })
  }
}

/**
 * Get the default base URL based on environment.
 * In development, the Vite dev server proxies /api to the backend.
 * In production, configure VITE_API_BASE_URL environment variable.
 */
function getDefaultBaseUrl(): string {
  // In production, use the configured API URL or empty string for same-origin
  if (import.meta.env.VITE_API_BASE_URL) {
    return import.meta.env.VITE_API_BASE_URL
  }

  // In development with Vite proxy, use relative URLs
  // The proxy in vite.config.ts handles forwarding to the backend
  return ''
}

/**
 * Extract a human-readable error message from various error types.
 */
function getErrorMessage(error: unknown): string {
  if (typeof error === 'string') {
    return error
  }

  if (error instanceof Error) {
    return error.message
  }

  if (error && typeof error === 'object') {
    // Handle common ASP.NET error response formats
    if ('title' in error && typeof error.title === 'string') {
      return error.title
    }
    if ('message' in error && typeof error.message === 'string') {
      return error.message
    }
    if ('detail' in error && typeof error.detail === 'string') {
      return error.detail
    }
    if ('errors' in error && typeof error.errors === 'object') {
      // ASP.NET validation errors
      const errors = error.errors as Record<string, string[]>
      const messages = Object.values(errors).flat()
      if (messages.length > 0) {
        return messages.join(', ')
      }
    }
  }

  return 'An unexpected error occurred'
}

/**
 * Extract the path from a URL, removing the domain and query parameters.
 */
function getPathFromUrl(url: string): string {
  try {
    const urlObj = new URL(url, window.location.origin)
    return urlObj.pathname
  } catch {
    // If URL parsing fails, try to extract path from string
    const match = url.match(/^(?:https?:\/\/[^/]+)?([^?#]*)/)?.[1]
    return match || url
  }
}

/**
 * Check if a URL should be excluded from telemetry tracking.
 * Excludes telemetry and health check endpoints.
 */
function isExcludedFromTelemetry(url: string): boolean {
  const path = getPathFromUrl(url)
  return (
    path.includes('/api/clienttelemetry') || path.includes('/health') || path.includes('/metrics')
  )
}

// Re-export the configured client and all generated types/SDK
export { client }
export * from './generated'
