import { describe, it, expect } from 'vitest'
import { renderPromptTemplate, type PromptContext } from './render-prompt-template'

describe('renderPromptTemplate', () => {
  const mockContext: PromptContext = {
    title: 'Add dark mode',
    id: 'ISSUE-123',
    description: 'Implement dark mode toggle',
    branch: 'feature/dark-mode',
    type: 'Feature',
  }

  it('returns empty string for null template', () => {
    expect(renderPromptTemplate(null, mockContext)).toBe('')
  })

  it('returns empty string for undefined template', () => {
    expect(renderPromptTemplate(undefined, mockContext)).toBe('')
  })

  it('returns empty string for empty template', () => {
    expect(renderPromptTemplate('', mockContext)).toBe('')
  })

  it('replaces {{title}} placeholder', () => {
    const result = renderPromptTemplate('Issue: {{title}}', mockContext)
    expect(result).toBe('Issue: Add dark mode')
  })

  it('replaces {{id}} placeholder', () => {
    const result = renderPromptTemplate('ID: {{id}}', mockContext)
    expect(result).toBe('ID: ISSUE-123')
  })

  it('replaces {{description}} placeholder', () => {
    const result = renderPromptTemplate('Desc: {{description}}', mockContext)
    expect(result).toBe('Desc: Implement dark mode toggle')
  })

  it('replaces {{branch}} placeholder', () => {
    const result = renderPromptTemplate('Branch: {{branch}}', mockContext)
    expect(result).toBe('Branch: feature/dark-mode')
  })

  it('replaces {{type}} placeholder', () => {
    const result = renderPromptTemplate('Type: {{type}}', mockContext)
    expect(result).toBe('Type: Feature')
  })

  it('handles case-insensitive placeholders', () => {
    const result = renderPromptTemplate('{{TITLE}} - {{Title}} - {{title}}', mockContext)
    expect(result).toBe('Add dark mode - Add dark mode - Add dark mode')
  })

  it('replaces multiple placeholders', () => {
    const template = '## {{title}}\n**ID:** {{id}}\n**Type:** {{type}}'
    const result = renderPromptTemplate(template, mockContext)
    expect(result).toBe('## Add dark mode\n**ID:** ISSUE-123\n**Type:** Feature')
  })

  it('preserves unknown placeholders', () => {
    const result = renderPromptTemplate('{{unknown}} text', mockContext)
    expect(result).toBe('{{unknown}} text')
  })

  it('handles empty context values', () => {
    const emptyContext: PromptContext = {
      title: '',
      id: '',
      description: '',
      branch: '',
      type: '',
    }
    const result = renderPromptTemplate('{{title}}-{{id}}', emptyContext)
    expect(result).toBe('-')
  })

  it('handles template with no placeholders', () => {
    const result = renderPromptTemplate('Just plain text', mockContext)
    expect(result).toBe('Just plain text')
  })

  it('handles multi-line template', () => {
    const template = `## Issue: {{title}}

**ID:** {{id}}
**Type:** {{type}}
**Branch:** {{branch}}

### Description
{{description}}`
    const result = renderPromptTemplate(template, mockContext)
    expect(result).toBe(`## Issue: Add dark mode

**ID:** ISSUE-123
**Type:** Feature
**Branch:** feature/dark-mode

### Description
Implement dark mode toggle`)
  })

  it('replaces {{context}} placeholder with context value', () => {
    const treeContext = `- parent1 [feature] [open] Parent Issue
  - child1 [task] [progress] Current Issue`
    const contextWithTree: PromptContext = {
      ...mockContext,
      context: treeContext,
    }
    const template = '## Hierarchy\n{{context}}\n\n## Title\n{{title}}'
    const result = renderPromptTemplate(template, contextWithTree)
    expect(result).toBe(`## Hierarchy\n${treeContext}\n\n## Title\nAdd dark mode`)
  })

  it('replaces {{context}} with empty string when undefined', () => {
    const result = renderPromptTemplate('Context: {{context}}', mockContext)
    expect(result).toBe('Context: ')
  })
})
