/**
 * Task Graph Konva components.
 *
 * Canvas-based visualization using React Konva with full edge paths.
 */

export { TaskGraphKonvaView } from './task-graph-konva-view'
export type { TaskGraphKonvaViewProps, TaskGraphKonvaViewRef } from './task-graph-konva-view'

export {
  KonvaIssueNode,
  KonvaEdge,
  KonvaHiddenParentIndicator,
  KonvaAgentStatusRing,
  KonvaDiagonalEdge,
} from './konva-nodes'
export { KonvaHtmlRow } from './konva-html-row'
export type { KonvaHtmlRowProps } from './konva-html-row'

export { useCamera, clampPosition } from './use-camera'
export type { CameraState, Size } from './use-camera'

export {
  useEdgePaths,
  computeEdgePaths,
  useDiagonalEdges,
  computeDiagonalEdges,
} from './use-edge-paths'
export type { EdgePath, DiagonalEdgePath } from './use-edge-paths'
