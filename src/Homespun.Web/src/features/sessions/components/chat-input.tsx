import { useCallback, useMemo, useRef } from 'react'
import {
  AssistantRuntimeProvider,
  ComposerPrimitive,
  Tools,
  useAui,
  useExternalStoreRuntime,
  type AppendMessage,
  type AssistantRuntime,
} from '@assistant-ui/react'
import { unstable_defaultDirectiveFormatter } from '@assistant-ui/core'
import type { Unstable_TriggerAdapter, Unstable_TriggerItem } from '@assistant-ui/core'
import { Send, Loader2, Shield, Sparkles, Hammer } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  ModelSelectorRoot,
  ModelSelectorTrigger,
  ModelSelectorContent,
} from '@/components/assistant-ui/model-selector'
import { useAvailableModels } from '@/features/agents/hooks'
import { useProjectFiles, useSearchablePrs } from '@/features/search'
import { cn } from '@/lib/utils'
import type { SessionMode } from '@/types/signalr'

/**
 * Composer for the session chat surface.
 *
 * Built on Assistant UI primitives:
 * - `ComposerPrimitive.Root` + `Input` + `Send` for the textarea + submit affordance
 * - `Unstable_TriggerPopoverRoot` + `Unstable_TriggerPopover` for `@`-mention and `/`-command popovers
 *
 * Mode (Plan/Build) and model selection are sibling controls above the input,
 * implemented via shadcn `Tabs` and `ModelSelector` (a thin wrapper around shadcn `Select`).
 *
 * The composer hosts its own minimal AUI runtime via `useExternalStoreRuntime` with empty
 * messages so it can live anywhere on the page (next to, not inside, `ChatSurface`). The
 * runtime's `onNew` callback invokes the consumer's `onSend(text, mode, model)`.
 */
export interface ChatInputProps {
  onSend: (message: string, sessionMode: SessionMode, model: string) => void
  sessionMode: SessionMode
  sessionModel: string
  onModeChange: (mode: SessionMode) => void
  onModelChange: (model: string) => void
  projectId?: string
  disabled?: boolean
  isLoading?: boolean
  placeholder?: string
}

export function ChatInput(props: ChatInputProps) {
  const { onSend, sessionMode, sessionModel } = props
  const sessionModeRef = useRef(sessionMode)
  const sessionModelRef = useRef(sessionModel)
  sessionModeRef.current = sessionMode
  sessionModelRef.current = sessionModel

  const sendMessage = useCallback(
    (text: string) => {
      onSend(text, sessionModeRef.current, sessionModelRef.current)
    },
    [onSend]
  )

  const runtime = useLocalComposerRuntime(sendMessage)
  const tools = useMemo(() => Tools({ toolkit: {} }), [])
  const aui = useAui({ tools })

  return (
    <AssistantRuntimeProvider runtime={runtime} aui={aui}>
      <ChatInputContent {...props} />
    </AssistantRuntimeProvider>
  )
}

function useLocalComposerRuntime(sendMessage: (text: string) => void): AssistantRuntime {
  return useExternalStoreRuntime<{ id: string; role: 'user'; content: [] }>({
    messages: [],
    isRunning: false,
    convertMessage: (m) => ({ id: m.id, role: m.role, content: m.content }),
    onNew: async (message: AppendMessage) => {
      const text = extractText(message)
      if (text.trim()) {
        sendMessage(text)
      }
    },
  })
}

function extractText(message: AppendMessage): string {
  const parts = message.content
  if (typeof parts === 'string') return parts
  return parts
    .filter((p): p is { type: 'text'; text: string } => p.type === 'text')
    .map((p) => p.text)
    .join('')
}

function ChatInputContent({
  sessionMode,
  sessionModel,
  onModeChange,
  onModelChange,
  projectId = '',
  disabled = false,
  isLoading = false,
  placeholder = 'Type a message...',
}: ChatInputProps) {
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Tab' && e.shiftKey) {
        e.preventDefault()
        const next: SessionMode = sessionMode === 'build' ? 'plan' : 'build'
        onModeChange(next)
      }
    },
    [sessionMode, onModeChange]
  )

  const { files } = useProjectFiles(projectId)
  const { prs } = useSearchablePrs(projectId)

  const fileItems = useMemo<readonly Unstable_TriggerItem[]>(
    () =>
      (files ?? []).map((path) => ({
        id: `file:${path}`,
        type: 'file',
        label: path,
        metadata: { path },
      })),
    [files]
  )
  const prItems = useMemo<readonly Unstable_TriggerItem[]>(
    () =>
      (prs ?? []).map((pr) => ({
        id: `pr:${pr.number}`,
        type: 'pr',
        label: `#${pr.number} ${pr.title ?? ''}`.trim(),
        description: pr.title ?? undefined,
        metadata: { number: pr.number ?? 0, title: pr.title ?? '' },
      })),
    [prs]
  )

  const mentionAdapter = useMemo<Unstable_TriggerAdapter>(
    () => ({
      categories: () =>
        [
          fileItems.length > 0 ? { id: 'files', label: 'Files' } : null,
          prItems.length > 0 ? { id: 'prs', label: 'Pull requests' } : null,
        ].filter((c): c is { id: string; label: string } => c !== null),
      categoryItems: (categoryId: string) => {
        if (categoryId === 'files') return fileItems
        if (categoryId === 'prs') return prItems
        return []
      },
      search: (query: string) => {
        const q = query.toLowerCase()
        const matches = (label: string) => label.toLowerCase().includes(q)
        return [
          ...fileItems.filter((item) => matches(item.label)),
          ...prItems.filter((item) => matches(item.label)),
        ].slice(0, 50)
      },
    }),
    [fileItems, prItems]
  )

  const mentionFormatter = useMemo(
    () => ({
      ...unstable_defaultDirectiveFormatter,
      serialize: (item: Unstable_TriggerItem) => {
        if (item.type === 'pr') {
          const num = (item.metadata as { number?: number } | undefined)?.number ?? 0
          return `PR #${num}`
        }
        const path = (item.metadata as { path?: string } | undefined)?.path ?? item.label
        return path.includes(' ') ? `@"${path}"` : `@${path}`
      },
    }),
    []
  )

  return (
    <ComposerPrimitive.Unstable_TriggerPopoverRoot>
      <ComposerPrimitive.Unstable_TriggerPopover
        char="@"
        adapter={mentionAdapter}
        className="bg-popover text-popover-foreground z-50 max-h-72 w-80 overflow-auto rounded-md border shadow-md"
      >
        <ComposerPrimitive.Unstable_TriggerPopover.Directive formatter={mentionFormatter} />
        <ComposerPrimitive.Unstable_TriggerPopoverBack className="hover:bg-accent hover:text-accent-foreground flex w-full items-center gap-1 px-2 py-1.5 text-left text-sm" />
        <ComposerPrimitive.Unstable_TriggerPopoverCategories>
          {(categories) =>
            categories.length === 0 ? (
              <div className="text-muted-foreground p-3 text-center text-sm">No matches</div>
            ) : (
              <ul className="p-1">
                {categories.map((cat) => (
                  <li key={cat.id}>
                    <ComposerPrimitive.Unstable_TriggerPopoverCategoryItem
                      categoryId={cat.id}
                      className="hover:bg-accent hover:text-accent-foreground data-[highlighted]:bg-accent data-[highlighted]:text-accent-foreground flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm"
                    >
                      {cat.label}
                    </ComposerPrimitive.Unstable_TriggerPopoverCategoryItem>
                  </li>
                ))}
              </ul>
            )
          }
        </ComposerPrimitive.Unstable_TriggerPopoverCategories>
        <ComposerPrimitive.Unstable_TriggerPopoverItems>
          {(items) =>
            items.length === 0 ? (
              <div className="text-muted-foreground p-3 text-center text-sm">No matches</div>
            ) : (
              <ul className="p-1">
                {items.map((item, index) => (
                  <li key={item.id}>
                    <ComposerPrimitive.Unstable_TriggerPopoverItem
                      item={item}
                      index={index}
                      className="hover:bg-accent hover:text-accent-foreground data-[highlighted]:bg-accent data-[highlighted]:text-accent-foreground flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm"
                    >
                      <span className="truncate">{item.label}</span>
                    </ComposerPrimitive.Unstable_TriggerPopoverItem>
                  </li>
                ))}
              </ul>
            )
          }
        </ComposerPrimitive.Unstable_TriggerPopoverItems>
      </ComposerPrimitive.Unstable_TriggerPopover>

      <ComposerPrimitive.Unstable_TriggerPopover
        char="/"
        adapter={SLASH_ADAPTER}
        className="bg-popover text-popover-foreground z-50 w-72 rounded-md border p-3 text-sm shadow-md"
      >
        <ComposerPrimitive.Unstable_TriggerPopover.Action onExecute={() => {}} />
        <p className="text-muted-foreground" data-testid="slash-empty-state">
          No commands available yet
        </p>
      </ComposerPrimitive.Unstable_TriggerPopover>

      <ComposerPrimitive.Root className="bg-background border-input focus-within:border-ring focus-within:ring-ring/50 flex w-full flex-col gap-2 rounded-2xl border p-2 shadow-xs focus-within:ring-[3px]">
        <div className="flex items-center justify-between px-1">
          <div className="flex items-center gap-2">
            <ModeTabs mode={sessionMode} onChange={onModeChange} disabled={disabled} />
            <ModelPicker value={sessionModel} onChange={onModelChange} disabled={disabled} />
          </div>
        </div>

        <ComposerPrimitive.Input
          placeholder={placeholder}
          disabled={disabled}
          rows={1}
          onKeyDown={handleKeyDown}
          className={cn(
            'placeholder:text-muted-foreground max-h-60 min-h-[44px] w-full resize-none bg-transparent px-2 py-2 text-sm leading-6 outline-none',
            disabled && 'cursor-not-allowed opacity-60'
          )}
        />

        <div className="flex items-center justify-end px-1 pb-1">
          <ComposerPrimitive.Send asChild>
            <Button
              type="submit"
              size="icon"
              variant="ghost"
              disabled={disabled}
              aria-label="Send message"
            >
              {isLoading ? (
                <Loader2 className="h-4 w-4 animate-spin" data-testid="send-loading" />
              ) : (
                <Send className="h-4 w-4" />
              )}
            </Button>
          </ComposerPrimitive.Send>
        </div>
      </ComposerPrimitive.Root>
    </ComposerPrimitive.Unstable_TriggerPopoverRoot>
  )
}

interface ModeTabsProps {
  mode: SessionMode
  onChange: (mode: SessionMode) => void
  disabled: boolean
}

function ModeTabs({ mode, onChange, disabled }: ModeTabsProps) {
  return (
    <Tabs
      value={mode}
      onValueChange={(v) => onChange(v as SessionMode)}
      className="!flex-row gap-0"
    >
      <TabsList variant="line" aria-label="Session mode" className="h-7">
        <TabsTrigger value="plan" disabled={disabled} className="gap-1 px-2 text-xs">
          <Shield className="h-3.5 w-3.5" />
          Plan
        </TabsTrigger>
        <TabsTrigger value="build" disabled={disabled} className="gap-1 px-2 text-xs">
          <Hammer className="h-3.5 w-3.5" />
          Build
        </TabsTrigger>
      </TabsList>
    </Tabs>
  )
}

interface ModelPickerProps {
  value: string
  onChange: (model: string) => void
  disabled: boolean
}

function ModelPicker({ value, onChange, disabled }: ModelPickerProps) {
  const { models, isLoading } = useAvailableModels()

  const options = useMemo(
    () =>
      models
        .filter((m): m is { id: string; displayName: string | null; createdAt: string } =>
          Boolean(m.id)
        )
        .map((m) => ({
          id: m.id,
          name: m.displayName ?? m.id,
        })),
    [models]
  )

  return (
    <ModelSelectorRoot
      models={options}
      value={value}
      onValueChange={onChange}
      // The shadcn Select primitive does not expose a `disabled` prop on Root;
      // disabled state propagates via the Trigger.
    >
      <ModelSelectorTrigger
        size="sm"
        variant="ghost"
        aria-label="Model selection"
        disabled={disabled || isLoading}
      >
        <span className="flex items-center gap-1">
          <Sparkles className="h-3.5 w-3.5" />
        </span>
      </ModelSelectorTrigger>
      <ModelSelectorContent />
    </ModelSelectorRoot>
  )
}

const SLASH_ADAPTER: Unstable_TriggerAdapter = {
  categories: () => [],
  categoryItems: () => [],
  search: () => [],
}
