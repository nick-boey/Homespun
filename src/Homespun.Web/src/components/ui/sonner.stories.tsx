import type { Meta, StoryObj } from '@storybook/react-vite'
import { toast } from 'sonner'

import { Button } from './button'
import { Toaster } from './sonner'

const meta: Meta<typeof Toaster> = {
  title: 'ui/Toaster',
  component: Toaster,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Toaster>

export const Default: Story = {
  render: () => (
    <div className="flex flex-col items-start gap-3">
      <div className="flex flex-wrap gap-2">
        <Button size="sm" onClick={() => toast('Default toast')}>
          Default
        </Button>
        <Button size="sm" variant="secondary" onClick={() => toast.success('Saved!')}>
          Success
        </Button>
        <Button size="sm" variant="destructive" onClick={() => toast.error('Something broke')}>
          Error
        </Button>
        <Button size="sm" variant="outline" onClick={() => toast.info('FYI')}>
          Info
        </Button>
      </div>
      <Toaster />
    </div>
  ),
}
