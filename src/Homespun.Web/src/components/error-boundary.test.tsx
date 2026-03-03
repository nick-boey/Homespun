import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ErrorBoundary, ErrorFallback, classifyError } from './error-boundary'

describe('classifyError', () => {
  it('returns "network" for network errors', () => {
    expect(classifyError(new Error('Failed to fetch'))).toBe('network')
    expect(classifyError(new Error('network error'))).toBe('network')
    expect(classifyError(new Error('net::ERR_CONNECTION_REFUSED'))).toBe('network')
    expect(classifyError(new Error('offline'))).toBe('network')

    const typeError = new TypeError('Failed to fetch')
    expect(classifyError(typeError)).toBe('network')
  })

  it('returns "timeout" for timeout errors', () => {
    expect(classifyError(new Error('Request timeout'))).toBe('timeout')
    expect(classifyError(new Error('Connection timed out'))).toBe('timeout')

    const timeoutError = new Error('timeout')
    timeoutError.name = 'TimeoutError'
    expect(classifyError(timeoutError)).toBe('timeout')
  })

  it('returns "api" for server errors', () => {
    expect(classifyError(new Error('API error'))).toBe('api')
    expect(classifyError(new Error('Server error'))).toBe('api')
    expect(classifyError(new Error('500 Internal Server Error'))).toBe('api')
    expect(classifyError(new Error('502 Bad Gateway'))).toBe('api')
    expect(classifyError(new Error('503 Service Unavailable'))).toBe('api')
    expect(classifyError(new Error('504 Gateway Timeout'))).toBe('api')
  })

  it('returns "unknown" for other errors', () => {
    expect(classifyError(new Error('Something went wrong'))).toBe('unknown')
    expect(classifyError(new Error('Invalid input'))).toBe('unknown')
    expect(classifyError(null)).toBe('unknown')
  })
})

describe('ErrorFallback', () => {
  describe('full variant', () => {
    it('renders with default content', () => {
      render(<ErrorFallback />)

      expect(screen.getByTestId('error-fallback')).toBeInTheDocument()
      expect(screen.getByText('Something went wrong')).toBeInTheDocument()
      expect(screen.getByText('Reload Page')).toBeInTheDocument()
    })

    it('displays custom title and description', () => {
      render(<ErrorFallback title="Custom Title" description="Custom description" />)

      expect(screen.getByText('Custom Title')).toBeInTheDocument()
      expect(screen.getByText('Custom description')).toBeInTheDocument()
    })

    it('displays error message as description', () => {
      const error = new Error('Specific error message')
      render(<ErrorFallback error={error} />)

      expect(screen.getByText('Specific error message')).toBeInTheDocument()
    })

    it('shows retry button when onRetry is provided', () => {
      const onRetry = vi.fn()
      render(<ErrorFallback onRetry={onRetry} />)

      const retryButton = screen.getByText('Try Again')
      expect(retryButton).toBeInTheDocument()

      fireEvent.click(retryButton)
      expect(onRetry).toHaveBeenCalledOnce()
    })

    it('disables retry button when isRetrying is true', () => {
      const onRetry = vi.fn()
      render(<ErrorFallback onRetry={onRetry} isRetrying={true} />)

      const retryButton = screen.getByRole('button', { name: /try again/i })
      expect(retryButton).toBeDisabled()
    })

    it('shows reset button when onReset is provided', () => {
      const onReset = vi.fn()
      render(<ErrorFallback onReset={onReset} />)

      const resetButton = screen.getByText('Reset')
      fireEvent.click(resetButton)
      expect(onReset).toHaveBeenCalledOnce()
    })

    it('applies error type data attribute', () => {
      render(<ErrorFallback errorType="network" />)

      expect(screen.getByTestId('error-fallback')).toHaveAttribute('data-error-type', 'network')
    })

    it('displays network error content for network errors', () => {
      const error = new Error('Failed to fetch')
      const onRetry = vi.fn()
      render(<ErrorFallback error={error} onRetry={onRetry} />)

      expect(screen.getByText('Connection Error')).toBeInTheDocument()
      expect(screen.getByText('Retry Connection')).toBeInTheDocument()
    })

    it('displays timeout error content for timeout errors', () => {
      const error = new Error('Request timeout')
      render(<ErrorFallback error={error} />)

      expect(screen.getByText('Request Timeout')).toBeInTheDocument()
    })

    it('displays api error content for server errors', () => {
      const error = new Error('500 Internal Server Error')
      render(<ErrorFallback error={error} />)

      expect(screen.getByText('Server Error')).toBeInTheDocument()
    })

    it('applies custom className', () => {
      render(<ErrorFallback className="custom-class" />)

      expect(screen.getByTestId('error-fallback')).toHaveClass('custom-class')
    })
  })

  describe('inline variant', () => {
    it('renders inline variant', () => {
      render(<ErrorFallback variant="inline" />)

      const container = screen.getByTestId('error-fallback')
      expect(container).toHaveClass('flex-col')
      expect(container).toHaveClass('rounded-lg')
      expect(container).toHaveClass('border')
    })

    it('shows retry button in inline variant', () => {
      const onRetry = vi.fn()
      render(<ErrorFallback variant="inline" onRetry={onRetry} />)

      expect(screen.getByRole('button')).toBeInTheDocument()
    })
  })

  describe('compact variant', () => {
    it('renders compact variant', () => {
      render(<ErrorFallback variant="compact" />)

      const container = screen.getByTestId('error-fallback')
      expect(container).toHaveClass('flex')
      expect(container).toHaveClass('items-center')
    })

    it('shows retry icon button in compact variant', () => {
      const onRetry = vi.fn()
      render(<ErrorFallback variant="compact" onRetry={onRetry} />)

      const retryButton = screen.getByRole('button')
      fireEvent.click(retryButton)
      expect(onRetry).toHaveBeenCalledOnce()
    })
  })
})

describe('ErrorBoundary', () => {
  // Suppress console.error for these tests
  const originalError = console.error
  beforeAll(() => {
    console.error = vi.fn()
  })
  afterAll(() => {
    console.error = originalError
  })

  const ThrowError = ({ shouldThrow }: { shouldThrow: boolean }) => {
    if (shouldThrow) {
      throw new Error('Test error')
    }
    return <div>Content</div>
  }

  it('renders children when no error', () => {
    render(
      <ErrorBoundary>
        <div>Child content</div>
      </ErrorBoundary>
    )

    expect(screen.getByText('Child content')).toBeInTheDocument()
  })

  it('renders fallback when error occurs', () => {
    render(
      <ErrorBoundary>
        <ThrowError shouldThrow={true} />
      </ErrorBoundary>
    )

    expect(screen.getByTestId('error-fallback')).toBeInTheDocument()
    expect(screen.getByText('Test error')).toBeInTheDocument()
  })

  it('renders custom fallback when provided', () => {
    render(
      <ErrorBoundary fallback={<div>Custom fallback</div>}>
        <ThrowError shouldThrow={true} />
      </ErrorBoundary>
    )

    expect(screen.getByText('Custom fallback')).toBeInTheDocument()
  })

  it('calls fallbackRender when error occurs', () => {
    const fallbackRender = vi.fn(({ error }) => <div>Error: {error.message}</div>)

    render(
      <ErrorBoundary fallbackRender={fallbackRender}>
        <ThrowError shouldThrow={true} />
      </ErrorBoundary>
    )

    expect(fallbackRender).toHaveBeenCalled()
    expect(screen.getByText('Error: Test error')).toBeInTheDocument()
  })

  it('calls onError when error is caught', () => {
    const onError = vi.fn()

    render(
      <ErrorBoundary onError={onError}>
        <ThrowError shouldThrow={true} />
      </ErrorBoundary>
    )

    expect(onError).toHaveBeenCalled()
    expect(onError.mock.calls[0][0].message).toBe('Test error')
  })

  it('calls onReset callback when reset button is clicked', () => {
    const onReset = vi.fn()

    render(
      <ErrorBoundary onReset={onReset}>
        <ThrowError shouldThrow={true} />
      </ErrorBoundary>
    )

    expect(screen.getByTestId('error-fallback')).toBeInTheDocument()

    // Click reset - this calls our onReset callback
    fireEvent.click(screen.getByText('Reset'))
    expect(onReset).toHaveBeenCalledOnce()
  })

  it('recovers after error is fixed and key changes', () => {
    // Using key prop to reset the error boundary
    const { rerender } = render(
      <ErrorBoundary key="boundary-1">
        <ThrowError shouldThrow={true} />
      </ErrorBoundary>
    )

    expect(screen.getByTestId('error-fallback')).toBeInTheDocument()

    // Rerender with a new key and a child that doesn't throw
    rerender(
      <ErrorBoundary key="boundary-2">
        <div>Recovered Content</div>
      </ErrorBoundary>
    )

    expect(screen.getByText('Recovered Content')).toBeInTheDocument()
  })
})
