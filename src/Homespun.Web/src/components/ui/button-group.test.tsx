import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import React from 'react'
import { ButtonGroup } from './button-group'
import { Button } from './button'

describe('ButtonGroup', () => {
  it('renders children', () => {
    render(
      <ButtonGroup>
        <Button>First</Button>
        <Button>Second</Button>
      </ButtonGroup>
    )

    expect(screen.getByRole('button', { name: 'First' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Second' })).toBeInTheDocument()
  })

  it('has role="group"', () => {
    render(
      <ButtonGroup>
        <Button>Test</Button>
      </ButtonGroup>
    )

    expect(screen.getByRole('group')).toBeInTheDocument()
  })

  it('applies custom className', () => {
    render(
      <ButtonGroup className="custom-class" data-testid="button-group">
        <Button>Test</Button>
      </ButtonGroup>
    )

    expect(screen.getByTestId('button-group')).toHaveClass('custom-class')
  })

  it('applies rounded corner styles via CSS classes', () => {
    render(
      <ButtonGroup data-testid="button-group">
        <Button>First</Button>
        <Button>Second</Button>
      </ButtonGroup>
    )

    const group = screen.getByTestId('button-group')
    expect(group).toHaveClass('[&>*]:rounded-none')
    expect(group).toHaveClass('[&>*:first-child]:rounded-l-md')
    expect(group).toHaveClass('[&>*:last-child]:rounded-r-md')
  })

  it('forwards ref to the div element', () => {
    const ref = React.createRef<HTMLDivElement>()
    render(
      <ButtonGroup ref={ref}>
        <Button>Test</Button>
      </ButtonGroup>
    )

    expect(ref.current).toBeInstanceOf(HTMLDivElement)
  })

  it('passes additional props to the div element', () => {
    render(
      <ButtonGroup data-testid="button-group" aria-label="Action buttons">
        <Button>Test</Button>
      </ButtonGroup>
    )

    expect(screen.getByTestId('button-group')).toHaveAttribute('aria-label', 'Action buttons')
  })
})
