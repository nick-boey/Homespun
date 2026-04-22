import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, screen, userEvent, within } from 'storybook/test'

import { Button } from './button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from './dialog'

const meta: Meta<typeof Dialog> = {
  title: 'ui/Dialog',
  component: Dialog,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Dialog>

const Demo = () => (
  <Dialog>
    <DialogTrigger asChild>
      <Button>Open dialog</Button>
    </DialogTrigger>
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Are you sure?</DialogTitle>
        <DialogDescription>This action cannot be undone.</DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <Button variant="outline">Cancel</Button>
        <Button>Confirm</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
)

export const Default: Story = { render: () => <Demo /> }

export const OpensOnTriggerClick: Story = {
  render: () => <Demo />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('button', { name: /open dialog/i }))
    await expect(screen.findByRole('dialog')).resolves.toBeVisible()
    await expect(screen.findByText('Are you sure?')).resolves.toBeVisible()
  },
}
