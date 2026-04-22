import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, screen, userEvent, within } from 'storybook/test'

import { Button } from './button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from './dropdown-menu'

const meta: Meta<typeof DropdownMenu> = {
  title: 'ui/DropdownMenu',
  component: DropdownMenu,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof DropdownMenu>

const Demo = () => (
  <DropdownMenu>
    <DropdownMenuTrigger asChild>
      <Button variant="outline">Open menu</Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent>
      <DropdownMenuLabel>Account</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuItem>Profile</DropdownMenuItem>
      <DropdownMenuItem>Settings</DropdownMenuItem>
      <DropdownMenuItem variant="destructive">Sign out</DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
)

export const Default: Story = { render: () => <Demo /> }

export const OpensOnTriggerClick: Story = {
  render: () => <Demo />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('button', { name: /open menu/i }))
    await expect(screen.findByRole('menu')).resolves.toBeVisible()
    await expect(screen.findByText('Profile')).resolves.toBeVisible()
  },
}
