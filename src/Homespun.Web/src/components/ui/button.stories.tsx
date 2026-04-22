import type { Meta, StoryObj } from '@storybook/react-vite'
import { Plus } from 'lucide-react'

import { Button } from './button'

const meta: Meta<typeof Button> = {
  title: 'ui/Button',
  component: Button,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Button>

export const Default: Story = {
  args: { children: 'Button' },
}

export const Variants: Story = {
  render: () => (
    <div className="flex flex-wrap items-center gap-2">
      <Button variant="default">Default</Button>
      <Button variant="destructive">Destructive</Button>
      <Button variant="outline">Outline</Button>
      <Button variant="secondary">Secondary</Button>
      <Button variant="ghost">Ghost</Button>
      <Button variant="link">Link</Button>
    </div>
  ),
}

export const Sizes: Story = {
  render: () => (
    <div className="flex flex-wrap items-center gap-2">
      <Button size="xs">xs</Button>
      <Button size="sm">sm</Button>
      <Button size="default">default</Button>
      <Button size="lg">lg</Button>
      <Button size="touch">touch</Button>
    </div>
  ),
}

export const IconSizes: Story = {
  render: () => (
    <div className="flex flex-wrap items-center gap-2">
      <Button size="icon-xs" aria-label="add">
        <Plus />
      </Button>
      <Button size="icon-sm" aria-label="add">
        <Plus />
      </Button>
      <Button size="icon" aria-label="add">
        <Plus />
      </Button>
      <Button size="icon-lg" aria-label="add">
        <Plus />
      </Button>
      <Button size="icon-touch" aria-label="add">
        <Plus />
      </Button>
    </div>
  ),
}

export const Disabled: Story = {
  args: { children: 'Disabled', disabled: true },
}
