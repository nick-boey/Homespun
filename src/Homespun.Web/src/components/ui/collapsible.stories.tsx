import type { Meta, StoryObj } from '@storybook/react-vite'

import { Button } from './button'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from './collapsible'

const meta: Meta<typeof Collapsible> = {
  title: 'ui/Collapsible',
  component: Collapsible,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Collapsible>

export const Default: Story = {
  render: () => (
    <Collapsible className="w-72">
      <CollapsibleTrigger asChild>
        <Button variant="outline" size="sm" className="w-full justify-between">
          Toggle details
        </Button>
      </CollapsibleTrigger>
      <CollapsibleContent className="bg-muted mt-2 rounded-md p-3 text-sm">
        Content revealed on expand. Uses `@radix-ui/react-collapsible`.
      </CollapsibleContent>
    </Collapsible>
  ),
}
