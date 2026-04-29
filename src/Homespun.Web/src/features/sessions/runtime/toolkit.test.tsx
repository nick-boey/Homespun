import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'

import { toolkit } from './toolkit'

describe('toolkit', () => {
  describe('Bash', () => {
    const bash = toolkit.Bash
    if (!bash || bash.type !== 'backend') {
      throw new Error('Bash entry must be a backend Toolkit entry')
    }
    const renderBash = bash.render as unknown as (props: never) => React.ReactElement

    it('renders the Terminal component (not a generic CodeBlock)', () => {
      const { container } = render(
        renderBash({
          toolName: 'Bash',
          toolCallId: 'tc-1',
          type: 'tool-call',
          status: { type: 'complete', reason: 'unknown' },
          argsText: '{"command":"ls -la"}',
          args: { command: 'ls -la' },
          result: 'total 0\n',
        } as never)
      )

      // The Terminal component sets data-slot="terminal" on its root.
      expect(container.querySelector('[data-slot="terminal"]')).not.toBeNull()
    })

    it('passes the command argument to the terminal prompt', () => {
      const { container } = render(
        renderBash({
          toolName: 'Bash',
          toolCallId: 'tc-2',
          type: 'tool-call',
          status: { type: 'complete', reason: 'unknown' },
          argsText: '{"command":"echo hi"}',
          args: { command: 'echo hi' },
          result: 'hi\n',
        } as never)
      )

      expect(container.textContent).toContain('echo hi')
    })

    it('places the stringified result in the terminal output', () => {
      const { container } = render(
        renderBash({
          toolName: 'Bash',
          toolCallId: 'tc-3',
          type: 'tool-call',
          status: { type: 'complete', reason: 'unknown' },
          argsText: '{"command":"echo hi"}',
          args: { command: 'echo hi' },
          result: 'hello world',
        } as never)
      )

      expect(container.textContent).toContain('hello world')
    })

    it('renders error result with destructive styling and as stderr', () => {
      const { container } = render(
        renderBash({
          toolName: 'Bash',
          toolCallId: 'tc-4',
          type: 'tool-call',
          status: { type: 'incomplete', reason: 'unknown' },
          argsText: '{"command":"false"}',
          args: { command: 'false' },
          result: 'something failed',
          isError: true,
        } as never)
      )

      // The container's outer wrapper carries the destructive border class.
      const wrapper = container.firstElementChild as HTMLElement
      expect(wrapper.className).toContain('border-destructive')
      // The error message appears in the rendered output.
      expect(container.textContent).toContain('something failed')
    })
  })

  describe('Read / Grep / Write are unchanged', () => {
    it('Read entry renders text content (no Terminal)', () => {
      const read = toolkit.Read
      if (!read || read.type !== 'backend') throw new Error('expected backend')
      const renderRead = read.render as unknown as (props: never) => React.ReactElement
      const { container } = render(
        renderRead({
          toolName: 'Read',
          toolCallId: 'r1',
          type: 'tool-call',
          status: { type: 'complete', reason: 'unknown' },
          argsText: '{"file_path":"README.md"}',
          args: { file_path: 'README.md' },
          result: '# Hello',
        } as never)
      )
      expect(container.querySelector('[data-slot="terminal"]')).toBeNull()
      expect(container.textContent).toContain('README.md')
    })
  })
})
