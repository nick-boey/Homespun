import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, screen, userEvent, within } from 'storybook/test'

import { Button } from './button'
import { Popover, PopoverContent, PopoverTrigger } from './popover'

const meta: Meta<typeof Popover> = {
  title: 'ui/Popover',
  component: Popover,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Popover>

const Demo = () => (
  <Popover>
    <PopoverTrigger asChild>
      <Button variant="outline">Open popover</Button>
    </PopoverTrigger>
    <PopoverContent className="w-64">
      <p className="text-sm">
        Popovers render into a portal. The trigger keeps keyboard focus on close.
      </p>
    </PopoverContent>
  </Popover>
)

export const Default: Story = { render: () => <Demo /> }

export const OpensOnTriggerClick: Story = {
  render: () => <Demo />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('button', { name: /open popover/i }))
    await expect(screen.findByText(/popovers render into a portal/i)).resolves.toBeVisible()
  },
}
