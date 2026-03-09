import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PrStatusIndicator } from './pr-status-indicator'

describe('PrStatusIndicator', () => {
  describe('merge conflict indicator', () => {
    it('shows green GitBranch icon when no conflicts', () => {
      render(<PrStatusIndicator checksPassing={true} hasConflicts={false} />)

      const branchIcon = screen.getByLabelText('No merge conflicts')
      expect(branchIcon).toBeInTheDocument()
      expect(branchIcon).toHaveClass('text-green-500')
    })

    it('shows red GitBranch icon when has conflicts', () => {
      render(<PrStatusIndicator checksPassing={true} hasConflicts={true} />)

      const branchIcon = screen.getByLabelText('Has merge conflicts')
      expect(branchIcon).toBeInTheDocument()
      expect(branchIcon).toHaveClass('text-red-500')
    })
  })

  describe('CI checks indicator', () => {
    it('shows green Check icon when tests passing', () => {
      render(<PrStatusIndicator checksPassing={true} hasConflicts={false} />)

      const checkIcon = screen.getByLabelText('Tests passing')
      expect(checkIcon).toBeInTheDocument()
      expect(checkIcon).toHaveClass('text-green-500')
    })

    it('shows red X icon when tests failing', () => {
      render(<PrStatusIndicator checksPassing={false} hasConflicts={false} />)

      const xIcon = screen.getByLabelText('Tests failing')
      expect(xIcon).toBeInTheDocument()
      expect(xIcon).toHaveClass('text-red-500')
    })

    it('shows yellow Loader2 spinner when tests running (null)', () => {
      render(<PrStatusIndicator checksPassing={null} hasConflicts={false} />)

      const spinnerIcon = screen.getByLabelText('Tests running')
      expect(spinnerIcon).toBeInTheDocument()
      expect(spinnerIcon).toHaveClass('text-yellow-500', 'animate-spin')
    })
  })

  describe('combined states', () => {
    it('shows both indicators correctly when has conflicts and tests passing', () => {
      render(<PrStatusIndicator checksPassing={true} hasConflicts={true} />)

      expect(screen.getByLabelText('Has merge conflicts')).toHaveClass('text-red-500')
      expect(screen.getByLabelText('Tests passing')).toHaveClass('text-green-500')
    })

    it('shows both indicators correctly when no conflicts and tests failing', () => {
      render(<PrStatusIndicator checksPassing={false} hasConflicts={false} />)

      expect(screen.getByLabelText('No merge conflicts')).toHaveClass('text-green-500')
      expect(screen.getByLabelText('Tests failing')).toHaveClass('text-red-500')
    })

    it('shows both indicators correctly when has conflicts and tests running', () => {
      render(<PrStatusIndicator checksPassing={null} hasConflicts={true} />)

      expect(screen.getByLabelText('Has merge conflicts')).toHaveClass('text-red-500')
      expect(screen.getByLabelText('Tests running')).toHaveClass('text-yellow-500', 'animate-spin')
    })
  })
})
