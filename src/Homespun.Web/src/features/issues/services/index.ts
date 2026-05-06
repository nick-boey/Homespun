// Task graph layout
export {
  computeLayout,
  computeLayoutFromIssues,
  TaskGraphMarkerType,
  isIssueRenderLine,
  getRenderKey,
  type ClientLayoutResult,
  type ComputeLayoutInput,
  type TaskGraphRenderLine,
  type TaskGraphIssueRenderLine,
  type TaskGraphEdge,
  type TaskGraphLayoutResult,
} from './task-graph-layout'

// Priority colors
export { getPriorityColor } from './priority-colors'

// Branch name generation
export { generateBranchName } from './branch-name'

// Inherited parent computation
export {
  computeInheritedParentInfo,
  computeInheritedParentInfoFromIssues,
  type InheritedParentInfo,
} from './inherited-parent'

// Filter query parser
export { parseFilterQuery, applyFilter, type ParsedFilter } from './filter-query-parser'

// Orphan change aggregation
export {
  aggregateOrphans,
  aggregateOrphansFromInputs,
  type OrphanEntry,
  type OrphanOccurrence,
} from './orphan-aggregation'
