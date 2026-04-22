import { describe, it, expect, vi } from 'vitest'
import { render } from '@testing-library/react'
import * as React from 'react'

const navigateSpy = vi.fn()

vi.mock('@tanstack/react-router', () => ({
  createFileRoute: vi.fn(() => (config: { component: React.ComponentType }) => ({
    component: config.component,
  })),
  Navigate: (props: { to: string; from?: string }) => {
    navigateSpy(props)
    return null
  },
}))

import { Route } from './projects.$projectId.index'

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const ProjectIndex = (Route as any).component as React.ComponentType

describe('ProjectIndex redirect', () => {
  it('redirects /projects/$projectId/ to /projects/$projectId/issues', () => {
    render(<ProjectIndex />)

    expect(navigateSpy).toHaveBeenCalledWith({
      to: '/projects/$projectId/issues',
      from: '/projects/$projectId/',
    })
  })
})
