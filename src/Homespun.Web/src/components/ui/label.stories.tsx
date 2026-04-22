import type { Meta, StoryObj } from '@storybook/react-vite'

import { Input } from './input'
import { Label } from './label'

const meta: Meta<typeof Label> = {
  title: 'ui/Label',
  component: Label,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Label>

export const Default: Story = {
  render: () => (
    <div className="flex flex-col gap-2">
      <Label htmlFor="email">Email</Label>
      <Input id="email" type="email" placeholder="you@example.com" />
    </div>
  ),
}
