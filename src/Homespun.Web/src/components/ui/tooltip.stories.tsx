import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, screen, userEvent, within } from 'storybook/test'

import { Button } from './button'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from './tooltip'

const meta: Meta<typeof Tooltip> = {
  title: 'ui/Tooltip',
  component: Tooltip,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Tooltip>

const Demo = () => (
  <TooltipProvider>
    <Tooltip>
      <TooltipTrigger asChild>
        <Button variant="outline">Hover me</Button>
      </TooltipTrigger>
      <TooltipContent>Tooltip content</TooltipContent>
    </Tooltip>
  </TooltipProvider>
)

export const Default: Story = { render: () => <Demo /> }

export const OpensOnHover: Story = {
  render: () => <Demo />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.hover(canvas.getByRole('button', { name: /hover me/i }))
    await expect(screen.findByText('Tooltip content')).resolves.toBeVisible()
  },
}
