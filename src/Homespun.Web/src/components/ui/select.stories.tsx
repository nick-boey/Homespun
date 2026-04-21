import type { Meta, StoryObj } from '@storybook/react-vite'

import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from './select'

const meta: Meta<typeof Select> = {
  title: 'ui/Select',
  component: Select,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Select>

export const Default: Story = {
  render: () => (
    <Select>
      <SelectTrigger className="w-56">
        <SelectValue placeholder="Pick a status" />
      </SelectTrigger>
      <SelectContent>
        <SelectGroup>
          <SelectLabel>Status</SelectLabel>
          <SelectItem value="open">Open</SelectItem>
          <SelectItem value="progress">In progress</SelectItem>
          <SelectItem value="review">In review</SelectItem>
          <SelectSeparator />
          <SelectItem value="complete">Complete</SelectItem>
        </SelectGroup>
      </SelectContent>
    </Select>
  ),
}
