import type { Meta, StoryObj } from '@storybook/react-vite'

import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from './table'

const meta: Meta<typeof Table> = {
  title: 'ui/Table',
  component: Table,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Table>

const rows = [
  { id: 'HS-001', title: 'Seed baseline', status: 'open' },
  { id: 'HS-002', title: 'Refactor theme', status: 'review' },
  { id: 'HS-003', title: 'Add Storybook', status: 'complete' },
]

export const Default: Story = {
  render: () => (
    <Table className="w-[32rem]">
      <TableCaption>Recent issues</TableCaption>
      <TableHeader>
        <TableRow>
          <TableHead>ID</TableHead>
          <TableHead>Title</TableHead>
          <TableHead>Status</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.id}>
            <TableCell className="font-mono text-xs">{row.id}</TableCell>
            <TableCell>{row.title}</TableCell>
            <TableCell>{row.status}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  ),
}
