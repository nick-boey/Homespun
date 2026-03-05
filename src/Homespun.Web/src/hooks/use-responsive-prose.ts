import { useMobile } from './use-mobile'

interface ResponsiveProseOptions {
  /**
   * Include the base 'prose' class before size modifiers
   * @default false
   */
  includeBase?: boolean

  /**
   * Include prose-invert for dark mode compatibility
   * @default false
   */
  invert?: boolean
}

/**
 * Hook that returns responsive prose classes based on viewport size.
 * Uses prose-sm for mobile viewports (<768px) and prose for larger viewports.
 *
 * @param options - Configuration options for prose classes
 * @returns A string containing the appropriate prose classes
 *
 * @example
 * // Basic usage
 * const proseClass = useResponsiveProse()
 * // Returns: 'prose-sm' on mobile, 'prose' on desktop
 *
 * @example
 * // With base class
 * const proseClass = useResponsiveProse({ includeBase: true })
 * // Returns: 'prose prose-sm' on mobile, 'prose' on desktop
 *
 * @example
 * // With dark mode support
 * const proseClass = useResponsiveProse({ invert: true })
 * // Returns: 'prose-sm prose-invert' on mobile, 'prose prose-invert' on desktop
 */
export function useResponsiveProse(options: ResponsiveProseOptions = {}): string {
  const { includeBase = false, invert = false } = options
  const isMobile = useMobile()

  const classes: string[] = []

  // Add base prose class if requested or if not using size modifier
  if (includeBase || (!isMobile && !includeBase)) {
    classes.push('prose')
  }

  // Add size modifier for mobile
  if (isMobile) {
    classes.push('prose-sm')
  }

  // Add invert modifier if requested
  if (invert) {
    classes.push('prose-invert')
  }

  return classes.join(' ')
}

/**
 * Export the index.ts file to include this hook
 */
export { useResponsiveProse as default }
