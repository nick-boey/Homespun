// Task graph layout
export {
  computeLayout,
  TaskGraphMarkerType,
  isIssueRenderLine,
  isPrRenderLine,
  isSeparatorRenderLine,
  isLoadMoreRenderLine,
  type TaskGraphRenderLine,
  type TaskGraphIssueRenderLine,
  type TaskGraphPrRenderLine,
  type TaskGraphSeparatorRenderLine,
  type TaskGraphLoadMoreRenderLine,
} from './task-graph-layout'

// Priority colors
export { getPriorityColor } from './priority-colors'

// Branch name generation
export { generateBranchName } from './branch-name'

// Inherited parent computation
export { computeInheritedParentInfo, type InheritedParentInfo } from './inherited-parent'

// Filter query parser
export { parseFilterQuery, applyFilter, type ParsedFilter } from './filter-query-parser'

// D3 layout for single SVG rendering
export {
  computeD3Layout,
  recalculateLayoutForExpansion,
  getContentWidth,
  getContentX,
  type D3TaskGraphNode,
  type D3TaskGraphEdge,
  type D3LayoutResult,
} from './task-graph-d3-layout'

// Edge routing for orthogonal paths
export {
  generateOrthogonalPath,
  generateSBendPath,
  generateVerticalLine,
  generateHorizontalLine,
  generateSeriesConnectorPath,
  generateParallelConnectorPath,
  generateParallelVerticalLine,
  generateLane0ConnectorPath,
  getLaneCenterX,
  getRowCenterY,
  findSafeVerticalChannel,
  routeBypassEdge,
  type RoutingNode,
  type RoutingEdge,
  type EdgeType,
} from './task-graph-edge-router'

// Animation helpers
export {
  animateNodes,
  animateEdges,
  animateSvgHeight,
  animateForeignObjects,
  staggeredTransition,
  cancelTransitions,
  hasActiveTransition,
  applyEnterAnimation,
  applyExitAnimation,
  onTransitionsComplete,
  TRANSITION_DURATION,
} from './task-graph-animations'
