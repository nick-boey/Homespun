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

  client.interceptors.request.use((request: Request) => {
    if (import.meta.env.DEV) {
      console.debug('[API Request]', request.method, request.url)
    }
    return request
  })

  client.interceptors.response.use((response: Response, request: Request) => {
    if (import.meta.env.DEV) {
      console.debug('[API Response]', request.method, request.url, response.status)
    }
    return response
  })

  client.interceptors.error.use(
    (error: unknown, response: Response | undefined, request: Request) => {
      if (response) {
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

        return new ApiClientError(
          apiError.status,
          apiError.statusText,
          apiError.message,
          apiError.details
        )
      }

      console.error('[API Network Error]', {
        url: request.url,
        error: error instanceof Error ? error.message : String(error),
      })

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

// Re-export the configured client and all generated types/SDK
export { client }
export * from './generated'
