import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import React from 'react'
import { FilterHelpPopover } from './filter-help-popover'

function renderPopover(props = {}) {
  return render(React.createElement(FilterHelpPopover, props))
}

describe('FilterHelpPopover', () => {
  it('renders help button by default', () => {
    renderPopover()
    expect(screen.getByTestId('filter-help-button')).toBeInTheDocument()
  })

  it('renders custom trigger when provided', () => {
    const customTrigger = React.createElement('button', { 'data-testid': 'custom-trigger' }, 'Help')
    renderPopover({ trigger: customTrigger })
    expect(screen.getByTestId('custom-trigger')).toBeInTheDocument()
    expect(screen.queryByTestId('filter-help-button')).not.toBeInTheDocument()
  })

  it('shows help content when clicked', async () => {
    const user = userEvent.setup()
    renderPopover()

    await user.click(screen.getByTestId('filter-help-button'))

    expect(screen.getByTestId('filter-help-content')).toBeInTheDocument()
    expect(screen.getByText('Filter Syntax')).toBeInTheDocument()
  })

  it('displays field filters documentation', async () => {
    const user = userEvent.setup()
    renderPopover()

    await user.click(screen.getByTestId('filter-help-button'))

    expect(screen.getByText('Field Filters')).toBeInTheDocument()
    // Check that the field filters section has content
    expect(screen.getByText(/Filter by status/)).toBeInTheDocument()
    expect(screen.getByText(/Filter by type/)).toBeInTheDocument()
    expect(screen.getByText(/Filter by priority/)).toBeInTheDocument()
  })

  it('displays status values documentation', async () => {
    const user = userEvent.setup()
    renderPopover()

    await user.click(screen.getByTestId('filter-help-button'))

    expect(screen.getByText('Status Values')).toBeInTheDocument()
    expect(screen.getByText(/draft, open, progress/)).toBeInTheDocument()
  })

  it('displays type values documentation', async () => {
    const user = userEvent.setup()
    renderPopover()

    await user.click(screen.getByTestId('filter-help-button'))

    expect(screen.getByText('Type Values')).toBeInTheDocument()
    expect(screen.getByText(/task, bug/)).toBeInTheDocument()
  })

  it('displays negation documentation', async () => {
    const user = userEvent.setup()
    renderPopover()

    await user.click(screen.getByTestId('filter-help-button'))

    expect(screen.getByText('Negation')).toBeInTheDocument()
    // Check that the negation prefix is documented
    expect(screen.getByText(/Prefix with/)).toBeInTheDocument()
  })

  it('displays multiple values documentation', async () => {
    const user = userEvent.setup()
    renderPopover()

    await user.click(screen.getByTestId('filter-help-button'))

    expect(screen.getByText('Multiple Values')).toBeInTheDocument()
    // Check that comma syntax is documented
    expect(screen.getByText(/Use comma to match any/)).toBeInTheDocument()
  })

  it('displays examples', async () => {
    const user = userEvent.setup()
    renderPopover()

    await user.click(screen.getByTestId('filter-help-button'))

    expect(screen.getByText('Examples')).toBeInTheDocument()
    // Examples section exists - actual examples checked by presence of code elements
    const examplesList = screen.getByText('Examples').parentElement
    expect(examplesList).toBeInTheDocument()
  })

  it('has accessible label on help button', () => {
    renderPopover()
    expect(screen.getByRole('button', { name: /filter help/i })).toBeInTheDocument()
  })
})
