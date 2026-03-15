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
}

/**
 * Renders a prompt template by replacing placeholders with context values.
 * Placeholders use the format {{name}} and are case-insensitive.
 *
 * Supported placeholders: {{title}}, {{id}}, {{description}}, {{branch}}, {{type}}
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

  return template.replace(/\{\{(\w+)\}\}/gi, (match, placeholder) => {
    const key = placeholder.toLowerCase() as keyof PromptContext
    if (key in context) {
      return context[key]
    }
    // Preserve unknown placeholders
    return match
  })
}
