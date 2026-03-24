import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WorkflowSettingsCard } from './workflow-settings-card'
import type { WorkflowDefinition } from '@/api/generated/types.gen'

const mockWorkflow: WorkflowDefinition = {
  id: 'wf-1',
  projectId: 'proj-1',
  title: 'Build Pipeline',
  description: 'Runs CI build for the project',
  steps: [],
  settings: {
    defaultTimeoutSeconds: 3600,
    continueOnFailure: false,
  },
  enabled: true,
  version: 3,
}

describe('WorkflowSettingsCard', () => {
  const defaultProps = {
    workflow: mockWorkflow,
    projectId: 'proj-1',
    onSave: vi.fn(),
    isSaving: false,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders with workflow data', () => {
    render(<WorkflowSettingsCard {...defaultProps} />)

    expect(screen.getByTestId('settings-title-input')).toHaveValue('Build Pipeline')
    expect(screen.getByTestId('settings-description-input')).toHaveValue(
      'Runs CI build for the project'
    )
    expect(screen.getByTestId('settings-timeout-input')).toHaveValue(3600)
  })

  it('renders with default values when settings are undefined', () => {
    const workflow: WorkflowDefinition = {
      ...mockWorkflow,
      settings: undefined,
      description: undefined,
    }
    render(<WorkflowSettingsCard {...defaultProps} workflow={workflow} />)

    expect(screen.getByTestId('settings-description-input')).toHaveValue('')
    expect(screen.getByTestId('settings-timeout-input')).toHaveValue(3600)
  })

  it('updates title input when edited', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    const titleInput = screen.getByTestId('settings-title-input')
    await user.clear(titleInput)
    await user.type(titleInput, 'New Title')

    expect(titleInput).toHaveValue('New Title')
  })

  it('updates description input when edited', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    const descInput = screen.getByTestId('settings-description-input')
    await user.clear(descInput)
    await user.type(descInput, 'New description')

    expect(descInput).toHaveValue('New description')
  })

  it('updates timeout input when changed', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    const timeoutInput = screen.getByTestId('settings-timeout-input')
    await user.clear(timeoutInput)
    await user.type(timeoutInput, '7200')

    expect(timeoutInput).toHaveValue(7200)
  })

  it('toggles continue on failure switch', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    const toggle = screen.getByTestId('settings-continue-on-failure-switch')
    expect(toggle).toHaveAttribute('data-state', 'unchecked')

    await user.click(toggle)

    expect(toggle).toHaveAttribute('data-state', 'checked')
  })

  it('toggles enabled switch', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    const toggle = screen.getByTestId('settings-enabled-switch')
    expect(toggle).toHaveAttribute('data-state', 'checked')

    await user.click(toggle)

    expect(toggle).toHaveAttribute('data-state', 'unchecked')
  })

  it('calls onSave with changed fields when save button is clicked', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    const titleInput = screen.getByTestId('settings-title-input')
    await user.clear(titleInput)
    await user.type(titleInput, 'Updated Title')

    await user.click(screen.getByTestId('settings-save-button'))

    expect(defaultProps.onSave).toHaveBeenCalledWith({
      title: 'Updated Title',
    })
  })

  it('calls onSave with multiple changed fields', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    const titleInput = screen.getByTestId('settings-title-input')
    await user.clear(titleInput)
    await user.type(titleInput, 'Updated Title')

    const descInput = screen.getByTestId('settings-description-input')
    await user.clear(descInput)
    await user.type(descInput, 'Updated description')

    await user.click(screen.getByTestId('settings-enabled-switch'))

    await user.click(screen.getByTestId('settings-save-button'))

    expect(defaultProps.onSave).toHaveBeenCalledWith({
      title: 'Updated Title',
      description: 'Updated description',
      enabled: false,
    })
  })

  it('calls onSave with settings changes', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    const timeoutInput = screen.getByTestId('settings-timeout-input')
    await user.clear(timeoutInput)
    await user.type(timeoutInput, '7200')

    await user.click(screen.getByTestId('settings-continue-on-failure-switch'))

    await user.click(screen.getByTestId('settings-save-button'))

    expect(defaultProps.onSave).toHaveBeenCalledWith({
      settings: {
        defaultTimeoutSeconds: 7200,
        continueOnFailure: true,
      },
    })
  })

  it('does not call onSave when nothing has changed', async () => {
    const user = userEvent.setup()
    render(<WorkflowSettingsCard {...defaultProps} />)

    await user.click(screen.getByTestId('settings-save-button'))

    expect(defaultProps.onSave).not.toHaveBeenCalled()
  })

  it('shows loading state when isSaving is true', () => {
    render(<WorkflowSettingsCard {...defaultProps} isSaving={true} />)

    const saveButton = screen.getByTestId('settings-save-button')
    expect(saveButton).toBeDisabled()
    expect(saveButton).toHaveTextContent('Saving…')
  })
})
