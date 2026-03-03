/**
 * API Module
 *
 * This module exports the configured API client, all generated SDK classes,
 * and TypeScript types for the Homespun API.
 *
 * Usage:
 * ```typescript
 * import { client, configureApiClient, Projects, Issues } from '@/api';
 *
 * // Configure the client (typically in main.tsx)
 * configureApiClient();
 *
 * // Use the SDK
 * const { data: projects } = await Projects.getApiProjects();
 * const { data: issues } = await Issues.getApiProjectsByProjectIdIssues({
 *   path: { projectId: 'my-project' }
 * });
 * ```
 */

export {
  // Client configuration
  client,
  configureApiClient,
  setAuthToken,
  ApiClientError,
  type ApiError,
} from './client'

// Re-export all generated SDK classes and types
export * from './generated'
