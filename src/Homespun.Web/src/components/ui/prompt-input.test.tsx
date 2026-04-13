import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import { PromptInput } from './prompt-input'

describe('PromptInput', () => {
  it('renders container with rounded-xl border radius', () => {
    const { container } = render(
      <PromptInput value="" onValueChange={() => {}}>
        <div>content</div>
      </PromptInput>
    )
    const outerDiv = container.querySelector('.rounded-xl')
    expect(outerDiv).toBeInTheDocument()
    expect(container.querySelector('.rounded-3xl')).not.toBeInTheDocument()
  })
})
