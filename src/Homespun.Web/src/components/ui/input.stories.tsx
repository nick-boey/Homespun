import type { Meta, StoryObj } from '@storybook/react-vite'

import { Input } from './input'

const meta: Meta<typeof Input> = {
  title: 'ui/Input',
  component: Input,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Input>

export const Default: Story = { args: { placeholder: 'Type here…' } }
export const Disabled: Story = { args: { placeholder: 'Disabled', disabled: true } }
export const WithValue: Story = { args: { defaultValue: 'homespun' } }
