// This file provides type aliases for backward compatibility.
// The actual AG-UI event types are now defined in Homespun.Shared.Models.Sessions.AGUIEvents.

// Re-export all AG-UI types from the shared project
global using AGUIEventType = Homespun.Shared.Models.Sessions.AGUIEventType;
global using AGUICustomEventName = Homespun.Shared.Models.Sessions.AGUICustomEventName;
global using AGUIBaseEvent = Homespun.Shared.Models.Sessions.AGUIBaseEvent;
global using RunStartedEvent = Homespun.Shared.Models.Sessions.RunStartedEvent;
global using RunFinishedEvent = Homespun.Shared.Models.Sessions.RunFinishedEvent;
global using RunErrorEvent = Homespun.Shared.Models.Sessions.RunErrorEvent;
global using TextMessageStartEvent = Homespun.Shared.Models.Sessions.TextMessageStartEvent;
global using TextMessageContentEvent = Homespun.Shared.Models.Sessions.TextMessageContentEvent;
global using TextMessageEndEvent = Homespun.Shared.Models.Sessions.TextMessageEndEvent;
global using ToolCallStartEvent = Homespun.Shared.Models.Sessions.ToolCallStartEvent;
global using ToolCallArgsEvent = Homespun.Shared.Models.Sessions.ToolCallArgsEvent;
global using ToolCallEndEvent = Homespun.Shared.Models.Sessions.ToolCallEndEvent;
global using ToolCallResultEvent = Homespun.Shared.Models.Sessions.ToolCallResultEvent;
global using StateSnapshotEvent = Homespun.Shared.Models.Sessions.StateSnapshotEvent;
global using StateDeltaEvent = Homespun.Shared.Models.Sessions.StateDeltaEvent;
global using CustomEvent = Homespun.Shared.Models.Sessions.CustomEvent;
global using AGUIEventFactory = Homespun.Shared.Models.Sessions.AGUIEventFactory;
global using AGUIPlanPendingData = Homespun.Shared.Models.Sessions.AGUIPlanPendingData;
