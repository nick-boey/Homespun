import type { Meta, StoryObj } from '@storybook/react-vite'

import { Button } from './button'
import { ButtonGroup } from './button-group'

const meta: Meta<typeof ButtonGroup> = {
  title: 'ui/ButtonGroup',
  component: ButtonGroup,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof ButtonGroup>

export const Default: Story = {
  render: () => (
    <ButtonGroup>
      <Button variant="outline">Left</Button>
      <Button variant="outline">Middle</Button>
      <Button variant="outline">Right</Button>
    </ButtonGroup>
  ),
}

export const TwoButtons: Story = {
  render: () => (
    <ButtonGroup>
      <Button variant="outline">Cancel</Button>
      <Button>Confirm</Button>
    </ButtonGroup>
  ),
}
