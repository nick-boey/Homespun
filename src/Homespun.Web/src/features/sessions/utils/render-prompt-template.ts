/**
 * Context values for rendering prompt templates.
 * These match the backend PromptContext placeholders.
 */
export interface PromptContext {
  title: string
  id: string
  description: string
  branch: string
  type: string
  /** Issue hierarchy showing ancestors and direct children */
  context?: string
  /** The selected issue ID for issue agent prompts */
  selectedIssueId?: string
}

/** Map lowercased placeholder names to context property names */
const PLACEHOLDER_KEY_MAP: Record<string, keyof PromptContext> = {
  title: 'title',
  id: 'id',
  description: 'description',
  branch: 'branch',
  type: 'type',
  context: 'context',
  selectedissueid: 'selectedIssueId',
}

/**
 * Renders a prompt template by replacing placeholders with context values.
 * Placeholders use the format {{name}} and are case-insensitive.
 * Supports conditional blocks: {{#if name}}content{{/if}}
 *
 * @param template - The template string with placeholders, or null/undefined
 * @param context - The context values to substitute
 * @returns The rendered string, or empty string if template is null/undefined/empty
 */
export function renderPromptTemplate(
  template: string | null | undefined,
  context: PromptContext
): string {
  if (!template) return ''

  // Process conditional blocks before placeholder replacement
  const result = template.replace(
    /\{\{#if (\w+)\}\}([\s\S]*?)\{\{\/if\}\}/gi,
    (_match, placeholder, content) => {
      const key = PLACEHOLDER_KEY_MAP[placeholder.toLowerCase()]
      return key && context[key] ? content : ''
    }
  )

  return result.replace(/\{\{(\w+)\}\}/gi, (match, placeholder) => {
    const key = PLACEHOLDER_KEY_MAP[placeholder.toLowerCase()]
    if (key) {
      return context[key] ?? ''
    }
    // Preserve unknown placeholders
    return match
  })
}
