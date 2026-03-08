import * as React from 'react'
import type { ToolExecutionGroup } from '@/types/tool-execution'
import { cn } from '@/lib/utils'
import { Collapsible, CollapsibleContent } from '@/components/ui/collapsible'
import { Button } from '@/components/ui/button'
import { ChevronDown, ChevronUp } from 'lucide-react'
import { ToolExecutionRow } from './tool-execution-row'

interface ToolExecutionGroupDisplayProps {
  group: ToolExecutionGroup
  className?: string
}

const DEFAULT_VISIBLE_TOOLS = 2

export function ToolExecutionGroupDisplay({ group, className }: ToolExecutionGroupDisplayProps) {
  const [isExpanded, setIsExpanded] = React.useState(false)

  const toolCount = group.executions.length
  const showExpandButton = toolCount > DEFAULT_VISIBLE_TOOLS
  const hiddenCount = toolCount - DEFAULT_VISIBLE_TOOLS

  // Determine which executions to show based on expanded state
  const visibleExecutions = isExpanded
    ? group.executions
    : group.executions.slice(-DEFAULT_VISIBLE_TOOLS)

  return (
    <div
      className={cn(
        'bg-secondary space-y-3 rounded-lg p-4',
        'bg-muted/50', // subtle background as per plan
        className
      )}
    >
      {/* Header */}
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium">
          {toolCount} tool {toolCount === 1 ? 'call' : 'calls'}
        </h3>

        {showExpandButton && (
          <Button
            variant="ghost"
            size="xs"
            onClick={() => setIsExpanded(!isExpanded)}
            aria-label={isExpanded ? 'Show less' : 'Show all'}
          >
            {isExpanded ? (
              <>
                Show less <ChevronUp className="ml-1" />
              </>
            ) : (
              <>
                Show all <ChevronDown className="ml-1" />
              </>
            )}
          </Button>
        )}
      </div>

      {/* Hidden tools notice */}
      {!isExpanded && hiddenCount > 0 && (
        <p className="text-muted-foreground text-xs">
          {hiddenCount} earlier tool {hiddenCount === 1 ? 'call' : 'calls'} hidden
        </p>
      )}

      {/* Tool executions list */}
      <div className="space-y-2">
        {isExpanded && toolCount > DEFAULT_VISIBLE_TOOLS ? (
          // Use Collapsible for smooth animation when showing all
          <Collapsible open={isExpanded}>
            <div className="space-y-2">
              {/* Show hidden tools first when expanded */}
              {group.executions.slice(0, -DEFAULT_VISIBLE_TOOLS).map((execution, index) => (
                <CollapsibleContent key={`${group.id}-${index}`}>
                  <ToolExecutionRow execution={execution} />
                </CollapsibleContent>
              ))}

              {/* Always visible tools */}
              {group.executions.slice(-DEFAULT_VISIBLE_TOOLS).map((execution, index) => (
                <ToolExecutionRow
                  key={`${group.id}-${group.executions.length - DEFAULT_VISIBLE_TOOLS + index}`}
                  execution={execution}
                />
              ))}
            </div>
          </Collapsible>
        ) : (
          // Simple list for 2 or fewer tools, or when collapsed
          visibleExecutions.map((execution, index) => (
            <ToolExecutionRow key={`${group.id}-${index}`} execution={execution} />
          ))
        )}
      </div>
    </div>
  )
}
