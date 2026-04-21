import type { Meta, StoryObj } from '@storybook/react-vite'

import { Label } from './label'
import { Switch } from './switch'

const meta: Meta<typeof Switch> = {
  title: 'ui/Switch',
  component: Switch,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Switch>

export const Default: Story = {
  render: () => (
    <div className="flex items-center gap-2">
      <Switch id="notifications" />
      <Label htmlFor="notifications">Notifications</Label>
    </div>
  ),
}

export const On: Story = {
  render: () => (
    <div className="flex items-center gap-2">
      <Switch id="notifications" defaultChecked />
      <Label htmlFor="notifications">On</Label>
    </div>
  ),
}

export const Disabled: Story = {
  render: () => (
    <div className="flex items-center gap-2">
      <Switch id="notifications" disabled />
      <Label htmlFor="notifications">Disabled</Label>
    </div>
  ),
}
