/**
 * Checks if the given element is an input element (input, textarea, select, or contenteditable).
 * Used to determine if keyboard shortcuts should be suppressed to allow normal typing.
 */
export function isInputElement(element: EventTarget | null): boolean {
  if (!element || !(element instanceof HTMLElement)) return false
  const tagName = element.tagName.toLowerCase()
  return (
    tagName === 'input' ||
    tagName === 'textarea' ||
    tagName === 'select' ||
    element.isContentEditable ||
    element.contentEditable === 'true'
  )
}
