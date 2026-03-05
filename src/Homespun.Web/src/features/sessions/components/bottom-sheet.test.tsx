import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { BottomSheet } from './bottom-sheet'

// Mock the sheet component
vi.mock('@/components/ui/sheet', () => ({
  Sheet: ({ children, open, onOpenChange, ...props }: any) => (
    <div data-testid="sheet" {...props}>
      {open && (
        <div
          data-testid="sheet-overlay"
          onClick={() => onOpenChange(false)}
        >
          {children}
        </div>
      )}
    </div>
  ),
  SheetContent: ({ children, className, ...props }: any) => (
    <div data-testid="sheet-content" className={className} {...props}>
      {children}
    </div>
  ),
  SheetHeader: ({ children }: any) => (
    <div data-testid="sheet-header">{children}</div>
  ),
  SheetTitle: ({ children }: any) => (
    <h2 data-testid="sheet-title">{children}</h2>
  ),
}))

describe('BottomSheet', () => {
  it('renders children when open', () => {
    render(
      <BottomSheet open={true} onOpenChange={() => {}}>
        <div>Test content</div>
      </BottomSheet>
    )

    expect(screen.getByText('Test content')).toBeInTheDocument()
  })

  it('does not render children when closed', () => {
    render(
      <BottomSheet open={false} onOpenChange={() => {}}>
        <div>Test content</div>
      </BottomSheet>
    )

    expect(screen.queryByText('Test content')).not.toBeInTheDocument()
  })

  it('calls onOpenChange when overlay is clicked', async () => {
    const onOpenChange = vi.fn()
    const user = userEvent.setup()

    render(
      <BottomSheet open={true} onOpenChange={onOpenChange}>
        <div>Test content</div>
      </BottomSheet>
    )

    // Find and click the overlay
    const overlay = screen.getByTestId('sheet-overlay')
    await user.click(overlay)

    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('renders with title when provided', () => {
    render(
      <BottomSheet open={true} onOpenChange={() => {}} title="Test Title">
        <div>Test content</div>
      </BottomSheet>
    )

    expect(screen.getByTestId('sheet-title')).toHaveTextContent('Test Title')
  })

  it('applies custom className', () => {
    render(
      <BottomSheet
        open={true}
        onOpenChange={() => {}}
        className="custom-class"
      >
        <div>Test content</div>
      </BottomSheet>
    )

    expect(screen.getByTestId('sheet-content')).toHaveClass('custom-class')
  })

  describe('swipe gestures', () => {
    it('closes when swiped down beyond threshold', async () => {
      const onOpenChange = vi.fn()

      render(
        <BottomSheet open={true} onOpenChange={onOpenChange}>
          <div data-testid="content">Test content</div>
        </BottomSheet>
      )

      const content = screen.getByTestId('sheet-content')

      // Simulate swipe down
      fireEvent.touchStart(content, {
        touches: [{ clientY: 100 }]
      })

      fireEvent.touchMove(content, {
        touches: [{ clientY: 250 }] // 150px down
      })

      fireEvent.touchEnd(content)

      await waitFor(() => {
        expect(onOpenChange).toHaveBeenCalledWith(false)
      })
    })

    it('does not close when swiped up', async () => {
      const onOpenChange = vi.fn()

      render(
        <BottomSheet open={true} onOpenChange={onOpenChange}>
          <div>Test content</div>
        </BottomSheet>
      )

      const content = screen.getByTestId('sheet-content')

      // Simulate swipe up
      fireEvent.touchStart(content, {
        touches: [{ clientY: 200 }]
      })

      fireEvent.touchMove(content, {
        touches: [{ clientY: 100 }] // 100px up
      })

      fireEvent.touchEnd(content)

      await waitFor(() => {
        expect(onOpenChange).not.toHaveBeenCalled()
      })
    })

    it('does not close when swipe is less than threshold', async () => {
      const onOpenChange = vi.fn()

      render(
        <BottomSheet open={true} onOpenChange={onOpenChange}>
          <div>Test content</div>
        </BottomSheet>
      )

      const content = screen.getByTestId('sheet-content')

      // Simulate small swipe down
      fireEvent.touchStart(content, {
        touches: [{ clientY: 100 }]
      })

      fireEvent.touchMove(content, {
        touches: [{ clientY: 120 }] // Only 20px down
      })

      fireEvent.touchEnd(content)

      await waitFor(() => {
        expect(onOpenChange).not.toHaveBeenCalled()
      }, { timeout: 100 })
    })

    it('applies transform style during swipe', () => {
      render(
        <BottomSheet open={true} onOpenChange={() => {}}>
          <div>Test content</div>
        </BottomSheet>
      )

      const content = screen.getByTestId('sheet-content')

      // Start swipe
      fireEvent.touchStart(content, {
        touches: [{ clientY: 100 }]
      })

      // Move down
      fireEvent.touchMove(content, {
        touches: [{ clientY: 150 }]
      })

      // Check that transform is applied
      expect(content).toHaveStyle({
        transform: 'translateY(50px)'
      })
    })

    it('resets transform after swipe ends', async () => {
      render(
        <BottomSheet open={true} onOpenChange={() => {}}>
          <div>Test content</div>
        </BottomSheet>
      )

      const content = screen.getByTestId('sheet-content')

      // Simulate swipe
      fireEvent.touchStart(content, {
        touches: [{ clientY: 100 }]
      })

      fireEvent.touchMove(content, {
        touches: [{ clientY: 120 }]
      })

      fireEvent.touchEnd(content)

      await waitFor(() => {
        expect(content).toHaveStyle({
          transform: 'translateY(0px)'
        })
      })
    })
  })

  describe('height stops', () => {
    it('supports peek height mode', () => {
      render(
        <BottomSheet
          open={true}
          onOpenChange={() => {}}
          heightMode="peek"
        >
          <div>Test content</div>
        </BottomSheet>
      )

      const content = screen.getByTestId('sheet-content')
      expect(content).toHaveClass('h-[100px]')
    })

    it('supports full height mode', () => {
      render(
        <BottomSheet
          open={true}
          onOpenChange={() => {}}
          heightMode="full"
        >
          <div>Test content</div>
        </BottomSheet>
      )

      const content = screen.getByTestId('sheet-content')
      expect(content).toHaveClass('h-[80vh]')
    })
  })
})