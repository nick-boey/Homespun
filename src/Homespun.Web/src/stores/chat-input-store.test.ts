import { describe, it, expect, beforeEach } from 'vitest'
import { useChatInputStore, type PermissionMode, type ModelSelection } from './chat-input-store'

describe('useChatInputStore', () => {
  beforeEach(() => {
    // Reset the store before each test
    useChatInputStore.setState({
      permissionMode: 'default',
      model: 'opus',
    })
  })

  describe('permissionMode', () => {
    it('has default permission mode as "default"', () => {
      const state = useChatInputStore.getState()
      expect(state.permissionMode).toBe('default')
    })

    it('can set permission mode to "bypass"', () => {
      useChatInputStore.getState().setPermissionMode('bypass')
      expect(useChatInputStore.getState().permissionMode).toBe('bypass')
    })

    it('can set permission mode to "accept-edits"', () => {
      useChatInputStore.getState().setPermissionMode('accept-edits')
      expect(useChatInputStore.getState().permissionMode).toBe('accept-edits')
    })

    it('can set permission mode to "plan"', () => {
      useChatInputStore.getState().setPermissionMode('plan')
      expect(useChatInputStore.getState().permissionMode).toBe('plan')
    })
  })

  describe('model', () => {
    it('has default model as "opus"', () => {
      const state = useChatInputStore.getState()
      expect(state.model).toBe('opus')
    })

    it('can set model to "sonnet"', () => {
      useChatInputStore.getState().setModel('sonnet')
      expect(useChatInputStore.getState().model).toBe('sonnet')
    })

    it('can set model to "haiku"', () => {
      useChatInputStore.getState().setModel('haiku')
      expect(useChatInputStore.getState().model).toBe('haiku')
    })
  })

  describe('type exports', () => {
    it('exports PermissionMode type with valid values', () => {
      const modes: PermissionMode[] = ['default', 'bypass', 'accept-edits', 'plan']
      expect(modes).toHaveLength(4)
    })

    it('exports ModelSelection type with valid values', () => {
      const models: ModelSelection[] = ['opus', 'sonnet', 'haiku']
      expect(models).toHaveLength(3)
    })
  })
})
