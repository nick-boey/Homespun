import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ClonesTab } from './clones-tab'

describe('ClonesTab', () => {
  it('renders the Clones heading', () => {
    render(<ClonesTab projectId="test-project-id" />)

    expect(screen.getByRole('heading', { name: 'Clones' })).toBeInTheDocument()
  })

  it('renders the placeholder message with projectId', () => {
    const projectId = 'my-project-123'
    render(<ClonesTab projectId={projectId} />)

    expect(screen.getByText(/Clone management coming soon/)).toBeInTheDocument()
    expect(screen.getByText(new RegExp(projectId))).toBeInTheDocument()
  })
})
