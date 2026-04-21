import type { Meta, StoryObj } from '@storybook/react-vite'

import { Textarea } from './textarea'

const meta: Meta<typeof Textarea> = {
  title: 'ui/Textarea',
  component: Textarea,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Textarea>

export const Default: Story = { args: { placeholder: 'Write a comment…' } }
export const Disabled: Story = { args: { placeholder: 'Disabled', disabled: true } }
