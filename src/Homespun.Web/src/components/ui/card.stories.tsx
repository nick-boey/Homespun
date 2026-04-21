import type { Meta, StoryObj } from '@storybook/react-vite'

import { Button } from './button'
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from './card'

const meta: Meta<typeof Card> = {
  title: 'ui/Card',
  component: Card,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Card>

export const Default: Story = {
  render: () => (
    <Card className="w-80">
      <CardHeader>
        <CardTitle>Project</CardTitle>
        <CardDescription>homespun — web client</CardDescription>
        <CardAction>
          <Button size="sm" variant="outline">
            Open
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        <p className="text-sm">Stories render shadcn primitives against the real Tailwind theme.</p>
      </CardContent>
      <CardFooter className="justify-end">
        <Button size="sm">Confirm</Button>
      </CardFooter>
    </Card>
  ),
}
