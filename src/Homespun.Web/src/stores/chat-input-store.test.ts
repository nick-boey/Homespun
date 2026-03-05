import { describe, it, expect, beforeEach } from 'vitest'
import { useChatInputStore, type ModelSelection } from './chat-input-store'
import { type SessionMode } from '@/types/signalr'

describe('useChatInputStore', () => {
  beforeEach(() => {
    // Reset the store before each test
    useChatInputStore.setState({
      sessionMode: 'Build',
      model: 'opus',
    })
  })

  describe('sessionMode', () => {
    it('has default session mode as "Build"', () => {
      const state = useChatInputStore.getState()
      expect(state.sessionMode).toBe('Build')
    })

    it('can set session mode to "Plan"', () => {
      useChatInputStore.getState().setSessionMode('Plan')
      expect(useChatInputStore.getState().sessionMode).toBe('Plan')
    })

    it('can set session mode to "Build"', () => {
      useChatInputStore.getState().setSessionMode('Build')
      expect(useChatInputStore.getState().sessionMode).toBe('Build')
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
    it('exports SessionMode type with valid values', () => {
      const modes: SessionMode[] = ['Build', 'Plan']
      expect(modes).toHaveLength(2)
    })

    it('exports ModelSelection type with valid values', () => {
      const models: ModelSelection[] = ['opus', 'sonnet', 'haiku']
      expect(models).toHaveLength(3)
    })
  })
})
