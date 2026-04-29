## MODIFIED Requirements

### Requirement: Tool-call rendering is wired through the Tool UI `Toolkit` API

The web client SHALL register one `Toolkit` entry per known built-in tool name (Bash, Read, Grep, Write), each declared `type: "backend"` with a `render({ result })` callback producing the tool-specific presentation. The toolkit SHALL be supplied to `useAui({ tools: Tools({ toolkit }) })` and the resulting `aui` SHALL be attached to `AssistantRuntimeProvider`. Unknown tool names SHALL render a generic fallback presentation. The `Bash` Toolkit entry SHALL render its output through the `Terminal` tool-ui component installed under `components/tool-ui/terminal/`, mapping the tool call's `command` argument to the terminal prompt and its `result` to the terminal output. Consecutive tool-call parts within a single assistant message SHALL render grouped under Assistant UI's `ToolGroup` primitive.

#### Scenario: Known tool renders its Toolkit `render` output
- **WHEN** a message contains a tool-call part with a `toolName` present in the Toolkit
- **THEN** the DOM SHALL render the output of that Toolkit entry's `render` callback
- **AND** the unknown-tool fallback SHALL NOT be used

#### Scenario: Unknown tool renders the fallback
- **WHEN** a message contains a tool-call part with a `toolName` not present in the Toolkit
- **THEN** the DOM SHALL render a generic unknown-tool fallback

#### Scenario: Runtime and Toolkit compose under one provider
- **WHEN** the session page mounts
- **THEN** a single `AssistantRuntimeProvider` SHALL receive both `runtime` (from `useExternalStoreRuntime`) and `aui` (from `useAui({ tools: Tools({ toolkit }) })`)
- **AND** a tool-call content part SHALL route to the Toolkit `render` callback without requiring the `useChatRuntime` path

#### Scenario: Bash tool renders through the Terminal component
- **WHEN** an assistant message contains a tool-call part with `toolName = "Bash"`, a `command` argument, and a `result`
- **THEN** the rendered DOM SHALL include the `Terminal` tool-ui component
- **AND** the terminal prompt SHALL display the `command` argument
- **AND** the terminal output SHALL display the tool-call `result`

#### Scenario: Consecutive tool calls render under ToolGroup
- **WHEN** an assistant message contains two or more consecutive tool-call parts before any text or reasoning part
- **THEN** those tool-call parts SHALL render inside a single `ToolGroup` container
- **AND** the relative order of tool calls SHALL match the source block order

#### Scenario: Single isolated tool call renders ungrouped
- **WHEN** an assistant message contains exactly one tool-call part adjacent to non-tool parts
- **THEN** that tool-call part SHALL render without a `ToolGroup` wrapper

### Requirement: Composer preserves session-control affordances

The Assistant UI composer SHALL be built on `ComposerPrimitive.Input` and SHALL present, as siblings of the input field, the session mode selector (Plan/Build) implemented as a `Tabs` primitive, the model selector implemented as a `Select`-style picker populated from the `useAvailableModels` hook, and the `@`-mention search popup attached via Assistant UI's `composer-trigger-popover` keyed on the `@` character. A second `composer-trigger-popover` keyed on `/` SHALL be present for forward-compatibility with a future slash-command catalogue, even when the catalogue is empty.

#### Scenario: Composer input is `ComposerPrimitive.Input`
- **WHEN** the chat input renders
- **THEN** the textarea element SHALL be the one rendered by `ComposerPrimitive.Input`
- **AND** message submission SHALL flow through `ComposerPrimitive.Send`

#### Scenario: Mode tabs toggle session mode
- **WHEN** the user activates the Plan or Build tab in the composer
- **THEN** the session-mode store SHALL be updated to the selected mode
- **AND** the next message sent SHALL carry the new mode

#### Scenario: Model selector toggles session model
- **WHEN** the user picks a model from the composer's model selector
- **THEN** the session-model store SHALL be updated
- **AND** the next message sent SHALL carry the new model selection
- **AND** the selector's option list SHALL be sourced from `useAvailableModels`

#### Scenario: `@`-mention triggers the search popup via composer-trigger-popover
- **WHEN** the user types `@` followed by partial text in the composer
- **THEN** the mention search popover SHALL appear at the trigger position via `composer-trigger-popover`
- **AND** selecting a result SHALL insert the referenced entity into the composer value at the trigger position
- **AND** the inserted text SHALL replace the `@` plus any partial query already typed

#### Scenario: `/` slash-command popover is present and stubbed
- **WHEN** the user types `/` at the start of a token in the composer
- **THEN** a `composer-trigger-popover` SHALL open at the trigger position
- **AND** the popover SHALL render an empty-state message indicating no commands are available

## ADDED Requirements

### Requirement: Assistant messages render without a bubble wrapper

`AssistantMessage` SHALL render its content parts directly on the page background, without any background-colour or padded-card wrapper enclosing the message as a whole. `UserMessage` SHALL retain its `bg-primary` right-aligned bubble. `SystemMessage` SHALL retain its centered `bg-muted` chip. Tool-call cards, code blocks, and reasoning surfaces inside an assistant message SHALL provide their own visible boundaries (border, background, or both) so that the absence of a parent bubble does not collapse their separation from surrounding text.

#### Scenario: Assistant message has no bubble wrapper
- **WHEN** an assistant message renders
- **THEN** the rendered DOM tree for that message SHALL NOT include a wrapping element with a background-fill class such as `bg-secondary`, `bg-card`, or `bg-muted`
- **AND** text parts SHALL appear directly against the page background

#### Scenario: User message keeps its bubble
- **WHEN** a user message renders
- **THEN** the message content SHALL be wrapped in a right-aligned element with `bg-primary` and `text-primary-foreground`

#### Scenario: System message keeps its chip
- **WHEN** a system message renders
- **THEN** the message content SHALL be wrapped in a centered element with `bg-muted` and italic text

#### Scenario: Inner surfaces retain their boundaries
- **WHEN** an assistant message contains a tool-call card, a fenced code block, or a reasoning surface
- **THEN** each such surface SHALL render with its own border or background fill so it remains visually distinct from adjacent prose

### Requirement: Reasoning parts use a collapsible reasoning surface

The web client SHALL render `reasoning` content parts via a collapsible reasoning component supplied to `MessagePrimitive.Parts`'s `Reasoning` slot, configured to collapse by default once a non-reasoning part appears in the same message and to remain expanded while it is the only streaming part.

#### Scenario: Reasoning collapses when text arrives
- **WHEN** an assistant message contains a reasoning part followed by a text part
- **THEN** the reasoning surface SHALL render in its collapsed state by default
- **AND** the user SHALL be able to expand it via its disclosure control

#### Scenario: Reasoning stays expanded while alone
- **WHEN** an assistant message contains only a reasoning part (no text or tool-call yet)
- **THEN** the reasoning surface SHALL render in its expanded state

#### Scenario: Reasoning content matches the source block
- **WHEN** a `thinking` block becomes a `reasoning` content part
- **THEN** the rendered reasoning surface SHALL display the block's text verbatim
