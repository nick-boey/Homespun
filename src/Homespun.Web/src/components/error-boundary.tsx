import * as React from 'react'
import { AlertCircle, RefreshCw, WifiOff, Clock, ServerCrash } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { getGlobalTelemetryService } from '@/lib/telemetry/telemetry-singleton'

/**
 * Error type classification for contextual error display.
 */
export type ErrorType = 'network' | 'api' | 'timeout' | 'unknown'

/**
 * Classify an error into a specific type for contextual display.
 */
export function classifyError(error: Error | null): ErrorType {
  if (!error) return 'unknown'

  const message = error.message.toLowerCase()

  // API/Server errors - check first because 504 contains "timeout"
  // These are HTTP status code errors from servers
  if (
    message.includes('api') ||
    message.includes('server') ||
    message.includes('500') ||
    message.includes('502') ||
    message.includes('503') ||
    message.includes('504')
  ) {
    return 'api'
  }

  // Network errors
  if (
    message.includes('network') ||
    message.includes('failed to fetch') ||
    message.includes('net::') ||
    message.includes('offline') ||
    error.name === 'TypeError'
  ) {
    return 'network'
  }

  // Timeout errors (non-HTTP timeout errors like client-side timeouts)
  if (
    message.includes('timeout') ||
    message.includes('timed out') ||
    error.name === 'TimeoutError'
  ) {
    return 'timeout'
  }

  return 'unknown'
}

/**
 * Get error display configuration based on error type.
 */
function getErrorConfig(errorType: ErrorType) {
  switch (errorType) {
    case 'network':
      return {
        icon: WifiOff,
        title: 'Connection Error',
        description: 'Unable to connect to the server. Please check your internet connection.',
        retryText: 'Retry Connection',
      }
    case 'timeout':
      return {
        icon: Clock,
        title: 'Request Timeout',
        description: 'The request took too long to complete. Please try again.',
        retryText: 'Retry',
      }
    case 'api':
      return {
        icon: ServerCrash,
        title: 'Server Error',
        description: 'Something went wrong on our end. Please try again later.',
        retryText: 'Retry',
      }
    default:
      return {
        icon: AlertCircle,
        title: 'Something went wrong',
        description: 'An unexpected error occurred.',
        retryText: 'Try Again',
      }
  }
}

export interface ErrorFallbackProps {
  /** The error that occurred */
  error?: Error | null
  /** Override the error type classification */
  errorType?: ErrorType
  /** Custom title override */
  title?: string
  /** Custom description override */
  description?: string
  /** Callback when retry is clicked */
  onRetry?: () => void
  /** Callback when reset is clicked */
  onReset?: () => void
  /** Whether a retry is in progress */
  isRetrying?: boolean
  /** Custom className for the container */
  className?: string
  /** Variant - 'full' for full page, 'inline' for inline display */
  variant?: 'full' | 'inline' | 'compact'
}

/**
 * Reusable error fallback component with retry mechanism.
 * Displays contextual error messages based on error type.
 */
export function ErrorFallback({
  error,
  errorType: errorTypeProp,
  title: titleProp,
  description: descriptionProp,
  onRetry,
  onReset,
  isRetrying = false,
  className,
  variant = 'full',
}: ErrorFallbackProps) {
  const errorType = errorTypeProp ?? classifyError(error ?? null)
  const config = getErrorConfig(errorType)

  const title = titleProp ?? config.title
  const description = descriptionProp ?? (error?.message || config.description)
  const Icon = config.icon

  if (variant === 'compact') {
    return (
      <div
        data-testid="error-fallback"
        data-error-type={errorType}
        className={cn(
          'border-destructive/50 bg-destructive/10 flex items-center gap-3 rounded-lg border p-4',
          className
        )}
      >
        <Icon className="text-destructive h-5 w-5 shrink-0" />
        <div className="min-w-0 flex-1">
          <p className="text-destructive text-sm font-medium">{title}</p>
          <p className="text-muted-foreground truncate text-xs">{description}</p>
        </div>
        {onRetry && (
          <Button
            variant="outline"
            size="sm"
            onClick={onRetry}
            disabled={isRetrying}
            className="shrink-0"
          >
            {isRetrying ? (
              <RefreshCw className="h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="h-4 w-4" />
            )}
          </Button>
        )}
      </div>
    )
  }

  if (variant === 'inline') {
    return (
      <div
        data-testid="error-fallback"
        data-error-type={errorType}
        className={cn(
          'border-destructive/50 bg-destructive/10 flex flex-col items-center justify-center rounded-lg border p-6 text-center',
          className
        )}
      >
        <Icon className="text-destructive h-8 w-8" />
        <h3 className="mt-3 text-lg font-semibold">{title}</h3>
        <p className="text-muted-foreground mt-1 max-w-md text-sm">{description}</p>
        {onRetry && (
          <Button variant="outline" className="mt-4" onClick={onRetry} disabled={isRetrying}>
            {isRetrying && <RefreshCw className="mr-2 h-4 w-4 animate-spin" />}
            {!isRetrying && <RefreshCw className="mr-2 h-4 w-4" />}
            {config.retryText}
          </Button>
        )}
      </div>
    )
  }

  // Full variant (default)
  return (
    <div
      data-testid="error-fallback"
      data-error-type={errorType}
      className={cn('flex min-h-[400px] flex-col items-center justify-center gap-4 p-8', className)}
    >
      <Icon className="text-destructive h-12 w-12" />
      <div className="text-center">
        <h2 className="text-foreground text-2xl font-semibold">{title}</h2>
        <p className="text-muted-foreground mt-2 max-w-md">{description}</p>
      </div>
      <div className="flex gap-2">
        {onRetry && (
          <Button onClick={onRetry} disabled={isRetrying}>
            {isRetrying && <RefreshCw className="mr-2 h-4 w-4 animate-spin" />}
            {!isRetrying && <RefreshCw className="mr-2 h-4 w-4" />}
            {config.retryText}
          </Button>
        )}
        {onReset && (
          <Button variant="outline" onClick={onReset}>
            Reset
          </Button>
        )}
        <Button variant="outline" onClick={() => window.location.reload()}>
          Reload Page
        </Button>
      </div>
      {import.meta.env.DEV && error && (
        <pre className="bg-muted text-muted-foreground mt-4 max-w-2xl overflow-auto rounded-md p-4 text-sm">
          {error.stack}
        </pre>
      )}
    </div>
  )
}

export interface ErrorBoundaryProps {
  children: React.ReactNode
  /** Custom fallback component */
  fallback?: React.ReactNode
  /** Fallback render function with error info */
  fallbackRender?: (props: { error: Error; resetErrorBoundary: () => void }) => React.ReactNode
  /** Callback when error is caught */
  onError?: (error: Error, errorInfo: React.ErrorInfo) => void
  /** Callback when error boundary is reset */
  onReset?: () => void
}

interface ErrorBoundaryState {
  hasError: boolean
  error: Error | null
}

export class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    console.error('ErrorBoundary caught an error:', error, errorInfo)

    // Track exception in telemetry
    const telemetry = getGlobalTelemetryService()
    if (telemetry) {
      const errorType = classifyError(error)
      telemetry.trackException(error, {
        errorBoundary: 'true',
        errorType,
        componentStack: errorInfo.componentStack || '',
        // Extract component name from stack if possible
        component: errorInfo.componentStack?.split('\n')[0]?.trim() || 'unknown',
      })
    }

    this.props.onError?.(error, errorInfo)
  }

  handleReset = () => {
    this.props.onReset?.()
    this.setState({ hasError: false, error: null })
  }

  render() {
    if (this.state.hasError) {
      // Use fallbackRender if provided
      if (this.props.fallbackRender && this.state.error) {
        return this.props.fallbackRender({
          error: this.state.error,
          resetErrorBoundary: this.handleReset,
        })
      }

      // Use static fallback if provided
      if (this.props.fallback) {
        return this.props.fallback
      }

      // Use default ErrorFallback
      return (
        <ErrorFallback
          error={this.state.error}
          onReset={this.handleReset}
          onRetry={this.handleReset}
        />
      )
    }

    return this.props.children
  }
}
