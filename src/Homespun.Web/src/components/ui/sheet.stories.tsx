import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, screen, userEvent, within } from 'storybook/test'

import { Button } from './button'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from './sheet'

const meta: Meta<typeof Sheet> = {
  title: 'ui/Sheet',
  component: Sheet,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Sheet>

const Demo = ({ side = 'right' as const }: { side?: 'top' | 'right' | 'bottom' | 'left' }) => (
  <Sheet>
    <SheetTrigger asChild>
      <Button variant="outline">Open sheet</Button>
    </SheetTrigger>
    <SheetContent side={side}>
      <SheetHeader>
        <SheetTitle>Settings</SheetTitle>
        <SheetDescription>Change your preferences and save.</SheetDescription>
      </SheetHeader>
    </SheetContent>
  </Sheet>
)

export const Default: Story = { render: () => <Demo /> }
export const Left: Story = { render: () => <Demo side="left" /> }

export const OpensOnTriggerClick: Story = {
  render: () => <Demo />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('button', { name: /open sheet/i }))
    await expect(screen.findByText('Settings')).resolves.toBeVisible()
  },
}
