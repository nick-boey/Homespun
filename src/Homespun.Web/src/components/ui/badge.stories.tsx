import type { Meta, StoryObj } from '@storybook/react-vite'

import { Badge } from './badge'

const meta: Meta<typeof Badge> = {
  title: 'ui/Badge',
  component: Badge,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Badge>

export const Default: Story = { args: { children: 'Badge' } }

export const Variants: Story = {
  render: () => (
    <div className="flex flex-wrap items-center gap-2">
      <Badge>Default</Badge>
      <Badge variant="secondary">Secondary</Badge>
      <Badge variant="destructive">Destructive</Badge>
      <Badge variant="outline">Outline</Badge>
      <Badge variant="ghost">Ghost</Badge>
      <Badge variant="link">Link</Badge>
    </div>
  ),
}
