import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import type { NavSection } from '@/lib/docs'

const sections: NavSection[] = [
  {
    title: 'Getting Started',
    items: [
      {
        slug: 'installation',
        title: 'Installation',
        description: 'Install guide',
        section: 'Getting Started',
        order: 0,
      },
      {
        slug: 'usage',
        title: 'Usage Guide',
        description: 'Usage guide',
        section: 'Getting Started',
        order: 1,
      },
    ],
  },
  {
    title: 'Guides',
    items: [
      {
        slug: 'multi-user',
        title: 'Multi-User Setup',
        description: 'Multi-user',
        section: 'Guides',
        order: 0,
      },
    ],
  },
]

function renderSidebar(path = '/') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Sidebar sections={sections} />
    </MemoryRouter>
  )
}

describe('Sidebar', () => {
  it('renders all section titles', () => {
    renderSidebar()
    expect(screen.getByText('Getting Started')).toBeInTheDocument()
    expect(screen.getByText('Guides')).toBeInTheDocument()
  })

  it('renders all navigation links', () => {
    renderSidebar()
    expect(screen.getByText('Installation')).toBeInTheDocument()
    expect(screen.getByText('Usage Guide')).toBeInTheDocument()
    expect(screen.getByText('Multi-User Setup')).toBeInTheDocument()
  })

  it('renders a Home link', () => {
    renderSidebar()
    expect(screen.getByText('Home')).toBeInTheDocument()
  })

  it('collapses sections when clicking the section toggle', async () => {
    const user = userEvent.setup()
    renderSidebar()

    expect(screen.getByText('Installation')).toBeInTheDocument()

    const toggle = screen.getByTestId('section-toggle-Getting Started')
    await user.click(toggle)

    expect(screen.queryByText('Installation')).not.toBeInTheDocument()
  })

  it('expands collapsed sections when clicking toggle again', async () => {
    const user = userEvent.setup()
    renderSidebar()

    const toggle = screen.getByTestId('section-toggle-Getting Started')
    await user.click(toggle)
    expect(screen.queryByText('Installation')).not.toBeInTheDocument()

    await user.click(toggle)
    expect(screen.getByText('Installation')).toBeInTheDocument()
  })
})
