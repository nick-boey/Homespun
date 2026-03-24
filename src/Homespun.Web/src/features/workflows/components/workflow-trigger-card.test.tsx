import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WorkflowTriggerCard } from './workflow-trigger-card'
import type { WorkflowTrigger } from '@/api/generated/types.gen'

describe('WorkflowTriggerCard', () => {
  const defaultOnChange = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders with manual trigger default when trigger is undefined', () => {
    render(<WorkflowTriggerCard trigger={undefined} onChange={defaultOnChange} />)

    expect(screen.getByTestId('trigger-type-manual')).toBeInTheDocument()
    expect(screen.getByTestId('trigger-type-manual')).not.toHaveClass('outline')
  })

  it('renders with provided trigger type', () => {
    const trigger: WorkflowTrigger = {
      type: 'event',
      enabled: true,
      eventConfig: { eventTypes: [] },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    expect(screen.getByTestId('event-config')).toBeInTheDocument()
  })

  it('shows event config when switching to event type', async () => {
    const user = userEvent.setup()
    render(<WorkflowTriggerCard trigger={undefined} onChange={defaultOnChange} />)

    await user.click(screen.getByTestId('trigger-type-event'))

    expect(defaultOnChange).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'event', eventConfig: { eventTypes: [] } })
    )
  })

  it('shows schedule config when switching to scheduled type', async () => {
    const user = userEvent.setup()
    render(<WorkflowTriggerCard trigger={undefined} onChange={defaultOnChange} />)

    await user.click(screen.getByTestId('trigger-type-scheduled'))

    expect(defaultOnChange).toHaveBeenCalledWith(
      expect.objectContaining({
        type: 'scheduled',
        scheduleConfig: { cronExpression: '', timezone: 'UTC', skipIfRunning: false },
      })
    )
  })

  it('shows webhook config when switching to webhook type', async () => {
    const user = userEvent.setup()
    render(<WorkflowTriggerCard trigger={undefined} onChange={defaultOnChange} />)

    await user.click(screen.getByTestId('trigger-type-webhook'))

    expect(defaultOnChange).toHaveBeenCalledWith(
      expect.objectContaining({
        type: 'webhook',
        webhookConfig: { secret: '', contentType: 'application/json' },
      })
    )
  })

  it('renders event config with checkboxes for event types', () => {
    const trigger: WorkflowTrigger = {
      type: 'event',
      enabled: true,
      eventConfig: { eventTypes: ['issueCreated'] },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    expect(screen.getByTestId('event-config')).toBeInTheDocument()
    expect(screen.getByTestId('event-type-issueCreated')).toBeInTheDocument()
    expect(screen.getByTestId('event-type-pullRequestOpened')).toBeInTheDocument()
  })

  it('toggles event types on checkbox click', async () => {
    const user = userEvent.setup()
    const trigger: WorkflowTrigger = {
      type: 'event',
      enabled: true,
      eventConfig: { eventTypes: ['issueCreated'] },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    await user.click(screen.getByTestId('event-type-pullRequestOpened'))

    expect(defaultOnChange).toHaveBeenCalledWith(
      expect.objectContaining({
        eventConfig: { eventTypes: ['issueCreated', 'pullRequestOpened'] },
      })
    )
  })

  it('removes event type when unchecking', async () => {
    const user = userEvent.setup()
    const trigger: WorkflowTrigger = {
      type: 'event',
      enabled: true,
      eventConfig: { eventTypes: ['issueCreated', 'pullRequestOpened'] },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    await user.click(screen.getByTestId('event-type-issueCreated'))

    expect(defaultOnChange).toHaveBeenCalledWith(
      expect.objectContaining({
        eventConfig: { eventTypes: ['pullRequestOpened'] },
      })
    )
  })

  it('renders schedule config with cron input', () => {
    const trigger: WorkflowTrigger = {
      type: 'scheduled',
      enabled: true,
      scheduleConfig: { cronExpression: '0 */6 * * *', timezone: 'UTC', skipIfRunning: false },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    expect(screen.getByTestId('schedule-config')).toBeInTheDocument()
    expect(screen.getByTestId('cron-expression-input')).toHaveValue('0 */6 * * *')
    expect(screen.getByTestId('timezone-input')).toHaveValue('UTC')
  })

  it('calls onChange when editing cron expression', async () => {
    const user = userEvent.setup()
    const trigger: WorkflowTrigger = {
      type: 'scheduled',
      enabled: true,
      scheduleConfig: { cronExpression: '', timezone: 'UTC', skipIfRunning: false },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    await user.type(screen.getByTestId('cron-expression-input'), '0')

    expect(defaultOnChange).toHaveBeenCalledWith(
      expect.objectContaining({
        scheduleConfig: expect.objectContaining({ cronExpression: '0' }),
      })
    )
  })

  it('renders webhook config with secret and content type', () => {
    const trigger: WorkflowTrigger = {
      type: 'webhook',
      enabled: true,
      webhookConfig: { secret: 'my-secret', contentType: 'application/json' },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    expect(screen.getByTestId('webhook-config')).toBeInTheDocument()
    expect(screen.getByTestId('webhook-secret-input')).toHaveValue('my-secret')
    expect(screen.getByTestId('webhook-content-type-input')).toHaveValue('application/json')
  })

  it('trigger enabled toggle calls onChange', async () => {
    const user = userEvent.setup()
    const trigger: WorkflowTrigger = { type: 'manual', enabled: true }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    await user.click(screen.getByTestId('trigger-enabled-switch'))

    expect(defaultOnChange).toHaveBeenCalledWith(expect.objectContaining({ enabled: false }))
  })

  it('trigger disabled toggle calls onChange with enabled true', async () => {
    const user = userEvent.setup()
    const trigger: WorkflowTrigger = { type: 'manual', enabled: false }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    await user.click(screen.getByTestId('trigger-enabled-switch'))

    expect(defaultOnChange).toHaveBeenCalledWith(expect.objectContaining({ enabled: true }))
  })

  it('webhook secret input is password type', () => {
    const trigger: WorkflowTrigger = {
      type: 'webhook',
      enabled: true,
      webhookConfig: { secret: 'secret', contentType: 'application/json' },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    expect(screen.getByTestId('webhook-secret-input')).toHaveAttribute('type', 'password')
  })

  it('renders skipIfRunning switch in schedule config', async () => {
    const user = userEvent.setup()
    const trigger: WorkflowTrigger = {
      type: 'scheduled',
      enabled: true,
      scheduleConfig: { cronExpression: '0 * * * *', timezone: 'UTC', skipIfRunning: false },
    }
    render(<WorkflowTriggerCard trigger={trigger} onChange={defaultOnChange} />)

    expect(screen.getByTestId('skip-if-running-switch')).toBeInTheDocument()

    await user.click(screen.getByTestId('skip-if-running-switch'))

    expect(defaultOnChange).toHaveBeenCalledWith(
      expect.objectContaining({
        scheduleConfig: expect.objectContaining({ skipIfRunning: true }),
      })
    )
  })
})
