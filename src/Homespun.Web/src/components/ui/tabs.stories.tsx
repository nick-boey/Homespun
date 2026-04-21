import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent, within } from 'storybook/test'

import { Tabs, TabsContent, TabsList, TabsTrigger } from './tabs'

const meta: Meta<typeof Tabs> = {
  title: 'ui/Tabs',
  component: Tabs,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Tabs>

const Demo = () => (
  <Tabs defaultValue="overview" className="w-80">
    <TabsList>
      <TabsTrigger value="overview">Overview</TabsTrigger>
      <TabsTrigger value="activity">Activity</TabsTrigger>
      <TabsTrigger value="settings">Settings</TabsTrigger>
    </TabsList>
    <TabsContent value="overview" className="pt-4 text-sm">
      Overview pane content.
    </TabsContent>
    <TabsContent value="activity" className="pt-4 text-sm">
      Activity pane content.
    </TabsContent>
    <TabsContent value="settings" className="pt-4 text-sm">
      Settings pane content.
    </TabsContent>
  </Tabs>
)

export const Default: Story = { render: () => <Demo /> }

export const SwitchesOnTabClick: Story = {
  render: () => <Demo />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('tab', { name: /activity/i }))
    await expect(canvas.getByText(/activity pane content/i)).toBeVisible()
  },
}
