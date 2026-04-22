import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent, within } from 'storybook/test'

import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
} from './command'

const meta: Meta<typeof Command> = {
  title: 'ui/Command',
  component: Command,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Command>

const Demo = () => (
  <Command className="w-80 rounded-md border">
    <CommandInput placeholder="Search commands…" />
    <CommandList>
      <CommandEmpty>No results.</CommandEmpty>
      <CommandGroup heading="Navigation">
        <CommandItem>Open projects</CommandItem>
        <CommandItem>Open sessions</CommandItem>
      </CommandGroup>
      <CommandSeparator />
      <CommandGroup heading="Actions">
        <CommandItem>New project</CommandItem>
        <CommandItem>Toggle theme</CommandItem>
      </CommandGroup>
    </CommandList>
  </Command>
)

export const Default: Story = { render: () => <Demo /> }

export const FiltersAsYouType: Story = {
  render: () => <Demo />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const input = canvas.getByPlaceholderText('Search commands…')
    await userEvent.type(input, 'theme')
    await expect(canvas.getByText(/toggle theme/i)).toBeVisible()
    await expect(canvas.queryByText(/open projects/i)).toBeNull()
  },
}
