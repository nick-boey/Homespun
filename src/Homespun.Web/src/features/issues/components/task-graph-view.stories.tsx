/**
 * Fixture-driven stories for the task graph rendering pipeline.
 *
 * Runs `computeLayoutFromIssues` against synthetic `IssueResponse` inputs so
 * the graph (rows + edges) renders live in Storybook without a server. Each
 * scenario is rendered in tree and next mode to catch divergence between the
 * two layout entry points. OpenSpec state attached to scenarios drives the
 * row's `OpenSpecIndicators` (branch dot + change-state glyph that opens the
 * phase-tree dialog) — phases no longer appear as graph rows.
 */

import { useMemo, useRef } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import {
  BranchPresence,
  ChangePhase,
  ExecutionMode,
  IssueStatus,
  IssueType,
  type IssueOpenSpecState,
  type IssueResponse,
} from '@/api'
import { ViewMode } from '../types'
import { computeLayoutFromIssues, isIssueRenderLine, getRenderKey } from '../services'
import { TaskGraphIssueRow } from './task-graph-row'
import { TaskGraphEdges } from './task-graph-svg'

// ---------------------------------------------------------------------------
// Fixture builders
// ---------------------------------------------------------------------------

function issue(
  id: string,
  overrides?: Partial<IssueResponse> & {
    parents?: Array<{ parentIssue: string; sortOrder?: string }>
  }
): IssueResponse {
  const { parents, ...rest } = overrides ?? {}
  return {
    id,
    title: id,
    description: `Description for ${id}`,
    status: IssueStatus.OPEN,
    type: IssueType.TASK,
    executionMode: ExecutionMode.SERIES,
    parentIssues: parents
      ? parents.map((p) => ({
          parentIssue: p.parentIssue,
          sortOrder: p.sortOrder ?? 'aaa',
        }))
      : null,
    ...rest,
  }
}

function openSpecState(
  options: {
    changeName?: string
    phases?: Array<{ name: string; done: number; total: number; tasks?: string[] }>
  } = {}
): IssueOpenSpecState {
  const { changeName = 'demo-change', phases } = options
  return {
    branchState: BranchPresence.WITH_CHANGE,
    changeState: phases ? ChangePhase.INCOMPLETE : ChangePhase.READY_TO_APPLY,
    changeName,
    schemaName: 'demo',
    phases:
      phases?.map((p) => ({
        name: p.name,
        done: p.done,
        total: p.total,
        tasks:
          p.tasks?.map((description, i) => ({
            description,
            done: i < p.done,
          })) ?? [],
      })) ?? null,
    orphans: null,
  }
}

const PROJECT_ID = 'storybook-project'

// ---------------------------------------------------------------------------
// Scenarios — each returns the inputs to the layout engine.
// ---------------------------------------------------------------------------

interface Scenario {
  issues: IssueResponse[]
  openSpecStates?: Record<string, IssueOpenSpecState>
}

/** 1. List of unrelated issues. */
function unrelatedIssues(): Scenario {
  return {
    issues: [
      issue('alpha', { title: 'Investigate flaky build' }),
      issue('beta', { title: 'Document deploy steps' }),
      issue('gamma', { title: 'Upgrade test runner' }),
    ],
  }
}

/** 2. Parent issue with 3 children (series). */
function parentSeries(prefix = 'series'): Scenario {
  const parentId = `${prefix}-parent`
  return {
    issues: [
      issue(parentId, {
        title: `${prefix} parent`,
        executionMode: ExecutionMode.SERIES,
      }),
      issue(`${prefix}-1`, {
        title: `${prefix} child 1`,
        parents: [{ parentIssue: parentId, sortOrder: 'aaa' }],
      }),
      issue(`${prefix}-2`, {
        title: `${prefix} child 2`,
        parents: [{ parentIssue: parentId, sortOrder: 'aab' }],
      }),
      issue(`${prefix}-3`, {
        title: `${prefix} child 3`,
        parents: [{ parentIssue: parentId, sortOrder: 'aac' }],
      }),
    ],
  }
}

/** 3. Parent issue with 3 children (parallel). */
function parentParallel(prefix = 'parallel'): Scenario {
  const parentId = `${prefix}-parent`
  return {
    issues: [
      issue(parentId, {
        title: `${prefix} parent`,
        executionMode: ExecutionMode.PARALLEL,
      }),
      issue(`${prefix}-1`, {
        title: `${prefix} child 1`,
        parents: [{ parentIssue: parentId, sortOrder: 'aaa' }],
      }),
      issue(`${prefix}-2`, {
        title: `${prefix} child 2`,
        parents: [{ parentIssue: parentId, sortOrder: 'aab' }],
      }),
      issue(`${prefix}-3`, {
        title: `${prefix} child 3`,
        parents: [{ parentIssue: parentId, sortOrder: 'aac' }],
      }),
    ],
  }
}

/** 4. Combined: a grandparent over the series + parallel parents above, plus an unrelated list. */
function combined(): Scenario {
  const grandparentId = 'root'
  const grandparent = issue(grandparentId, {
    title: 'Top-level epic',
    executionMode: ExecutionMode.SERIES,
  })

  const series = parentSeries('series')
  const parallel = parentParallel('parallel')

  // Re-parent the series + parallel parents under the grandparent.
  const seriesIssues = series.issues.map((it) =>
    it.id === 'series-parent'
      ? {
          ...it,
          parentIssues: [{ parentIssue: grandparentId, sortOrder: 'aaa' }],
        }
      : it
  )
  const parallelIssues = parallel.issues.map((it) =>
    it.id === 'parallel-parent'
      ? {
          ...it,
          parentIssues: [{ parentIssue: grandparentId, sortOrder: 'aab' }],
        }
      : it
  )

  const unrelated = unrelatedIssues().issues

  return {
    issues: [grandparent, ...seriesIssues, ...parallelIssues, ...unrelated],
  }
}

/** 5. Single issue with an OpenSpec change linked but no phases. */
function openSpecNoPhases(): Scenario {
  const id = 'spec-no-phases'
  return {
    issues: [issue(id, { title: 'Issue with linked change (no phases)' })],
    openSpecStates: {
      [id]: openSpecState({ changeName: 'add-feature-x' }),
    },
  }
}

/** 6. Single issue with an OpenSpec change linked + 3 phases. */
function openSpecThreePhases(idOverride?: string): Scenario {
  const id = idOverride ?? 'spec-three-phases'
  return {
    issues: [issue(id, { title: 'Issue with linked change + 3 phases' })],
    openSpecStates: {
      [id]: openSpecState({
        changeName: 'rework-pipeline',
        phases: [
          {
            name: 'Discovery',
            done: 2,
            total: 2,
            tasks: ['Audit existing pipeline', 'Identify bottlenecks'],
          },
          {
            name: 'Implementation',
            done: 1,
            total: 3,
            tasks: ['Write new module', 'Wire up tests', 'Migrate consumers'],
          },
          {
            name: 'Rollout',
            done: 0,
            total: 2,
            tasks: ['Deploy to staging', 'Cut over production'],
          },
        ],
      }),
    },
  }
}

/**
 * 7. Combined scenario with one issue at each of the three levels owning an
 *    OpenSpec change with 3 phases. Levels: grandparent (`root`),
 *    parent (`series-parent`), leaf (`parallel-2`).
 */
function combinedWithOpenSpec(): Scenario {
  const base = combined()
  const phasesFor = (changeName: string) => ({
    [changeName]: openSpecState({
      changeName,
      phases: [
        {
          name: 'Phase 1',
          done: 1,
          total: 2,
          tasks: ['Phase 1 task A', 'Phase 1 task B'],
        },
        {
          name: 'Phase 2',
          done: 0,
          total: 3,
          tasks: ['Phase 2 task A', 'Phase 2 task B', 'Phase 2 task C'],
        },
        {
          name: 'Phase 3',
          done: 0,
          total: 1,
          tasks: ['Phase 3 task A'],
        },
      ],
    }),
  })

  return {
    issues: base.issues,
    openSpecStates: {
      ...phasesFor('root'),
      ...phasesFor('series-parent'),
      ...phasesFor('parallel-2'),
    },
  }
}

// ---------------------------------------------------------------------------
// Presentational view — runs the layout engine and renders rows + edges.
// ---------------------------------------------------------------------------

interface FixtureGraphViewProps extends Scenario {
  viewMode: ViewMode
}

function FixtureGraphView({ issues, openSpecStates, viewMode }: FixtureGraphViewProps) {
  const layout = useMemo(
    () =>
      computeLayoutFromIssues({
        issues,
        viewMode,
      }),
    [issues, viewMode]
  )

  const renderLines = layout.lines
  const edges = layout.ok ? layout.edges : []
  const rowRefs = useRef<Map<string, HTMLDivElement>>(new Map())

  const maxLanes = useMemo(() => {
    return Math.max(1, ...renderLines.filter(isIssueRenderLine).map((line) => line.lane + 1))
  }, [renderLines])

  const openSpecByIssueId = openSpecStates ?? {}

  return (
    <div
      data-testid="task-graph-fixture"
      className="bg-background text-foreground w-[640px] overflow-x-auto p-2"
    >
      <div style={{ position: 'relative' }}>
        <TaskGraphEdges
          edges={edges}
          renderLines={renderLines}
          expandedIds={new Set()}
          maxLanes={maxLanes}
          rowRefs={rowRefs}
        />
        {renderLines.map((line, index) => {
          if (isIssueRenderLine(line)) {
            const renderKey = getRenderKey(line)
            return (
              <TaskGraphIssueRow
                key={renderKey}
                ref={(el) => {
                  if (el) rowRefs.current.set(renderKey, el)
                  else rowRefs.current.delete(renderKey)
                }}
                line={line}
                maxLanes={maxLanes}
                projectId={PROJECT_ID}
                openSpecState={openSpecByIssueId[line.issueId] ?? null}
                showActions={false}
                aria-rowindex={index + 1}
              />
            )
          }
          return null
        })}
      </div>
      {!layout.ok && (
        <div
          role="alert"
          className="border-destructive/40 bg-destructive/10 text-destructive mt-4 rounded border p-3 text-sm"
        >
          Cycle: {layout.cycle.join(' → ')}
        </div>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Storybook wiring
// ---------------------------------------------------------------------------

function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        // Linked-PR status hook fires `useQuery`; gate it from network so the
        // story doesn't poll a non-existent server.
        staleTime: Infinity,
        refetchInterval: false,
        refetchOnMount: false,
        refetchOnReconnect: false,
        refetchOnWindowFocus: false,
      },
    },
  })
}

const meta: Meta<typeof FixtureGraphView> = {
  title: 'features/issues/TaskGraphView/Scenarios',
  component: FixtureGraphView,
  parameters: { layout: 'padded' },
  decorators: [
    (Story) => (
      <QueryClientProvider client={makeQueryClient()}>
        <Story />
      </QueryClientProvider>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof FixtureGraphView>

// ---- 1. Unrelated issues -------------------------------------------------

export const UnrelatedIssuesTree: Story = {
  args: { ...unrelatedIssues(), viewMode: ViewMode.Tree },
}

export const UnrelatedIssuesNext: Story = {
  args: { ...unrelatedIssues(), viewMode: ViewMode.Next },
}

// ---- 2. Series parent ----------------------------------------------------

export const SeriesParentTree: Story = {
  args: { ...parentSeries(), viewMode: ViewMode.Tree },
}

export const SeriesParentNext: Story = {
  args: { ...parentSeries(), viewMode: ViewMode.Next },
}

// ---- 3. Parallel parent --------------------------------------------------

export const ParallelParentTree: Story = {
  args: { ...parentParallel(), viewMode: ViewMode.Tree },
}

export const ParallelParentNext: Story = {
  args: { ...parentParallel(), viewMode: ViewMode.Next },
}

// ---- 4. Combined ---------------------------------------------------------

export const CombinedTree: Story = {
  args: { ...combined(), viewMode: ViewMode.Tree },
}

export const CombinedNext: Story = {
  args: { ...combined(), viewMode: ViewMode.Next },
}

// ---- 5. OpenSpec change, no phases --------------------------------------

export const OpenSpecNoPhasesTree: Story = {
  args: { ...openSpecNoPhases(), viewMode: ViewMode.Tree },
}

export const OpenSpecNoPhasesNext: Story = {
  args: { ...openSpecNoPhases(), viewMode: ViewMode.Next },
}

// ---- 6. OpenSpec change with 3 phases -----------------------------------

export const OpenSpecThreePhasesTree: Story = {
  args: { ...openSpecThreePhases(), viewMode: ViewMode.Tree },
}

export const OpenSpecThreePhasesNext: Story = {
  args: { ...openSpecThreePhases(), viewMode: ViewMode.Next },
}

// ---- 7. Combined with OpenSpec at each level ----------------------------

export const CombinedWithOpenSpecTree: Story = {
  args: { ...combinedWithOpenSpec(), viewMode: ViewMode.Tree },
}

export const CombinedWithOpenSpecNext: Story = {
  args: { ...combinedWithOpenSpec(), viewMode: ViewMode.Next },
}
