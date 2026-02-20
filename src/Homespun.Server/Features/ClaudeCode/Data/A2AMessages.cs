using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// A2A Protocol event types.
/// See: https://a2a-protocol.org/latest/specification/
/// </summary>
public static class A2AEventKind
{
    public const string Task = "task";
    public const string Message = "message";
    public const string StatusUpdate = "status-update";
    public const string ArtifactUpdate = "artifact-update";
}

/// <summary>
/// A2A Task states representing the lifecycle of a task.
/// </summary>
public static class A2ATaskState
{
    public const string Submitted = "submitted";
    public const string Working = "working";
    public const string InputRequired = "input-required";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Rejected = "rejected";
    public const string AuthRequired = "auth-required";
    public const string Unknown = "unknown";
}

/// <summary>
/// Base type for A2A protocol events.
/// Uses the 'kind' discriminator to identify event type.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(A2ATask), A2AEventKind.Task)]
[JsonDerivedType(typeof(A2AMessage), A2AEventKind.Message)]
[JsonDerivedType(typeof(A2ATaskStatusUpdateEvent), A2AEventKind.StatusUpdate)]
[JsonDerivedType(typeof(A2ATaskArtifactUpdateEvent), A2AEventKind.ArtifactUpdate)]
public abstract record A2AEvent
{
    [JsonPropertyName("kind")]
    public abstract string Kind { get; }
}

/// <summary>
/// A2A Task representing a unit of work with lifecycle state.
/// </summary>
public record A2ATask : A2AEvent
{
    public override string Kind => A2AEventKind.Task;

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("contextId")]
    public required string ContextId { get; init; }

    [JsonPropertyName("status")]
    public required A2ATaskStatus Status { get; init; }

    [JsonPropertyName("artifacts")]
    public List<A2AArtifact>? Artifacts { get; init; }

    [JsonPropertyName("history")]
    public List<A2AMessage>? History { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// A2A Message representing a conversation turn.
/// </summary>
public record A2AMessage : A2AEvent
{
    public override string Kind => A2AEventKind.Message;

    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("parts")]
    public required List<A2APart> Parts { get; init; }

    [JsonPropertyName("contextId")]
    public required string ContextId { get; init; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; init; }

    [JsonPropertyName("referenceTaskIds")]
    public List<string>? ReferenceTaskIds { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// A2A Task status update event for real-time state changes.
/// </summary>
public record A2ATaskStatusUpdateEvent : A2AEvent
{
    public override string Kind => A2AEventKind.StatusUpdate;

    [JsonPropertyName("taskId")]
    public required string TaskId { get; init; }

    [JsonPropertyName("contextId")]
    public required string ContextId { get; init; }

    [JsonPropertyName("status")]
    public required A2ATaskStatus Status { get; init; }

    [JsonPropertyName("final")]
    public required bool Final { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// A2A Task artifact update event for generated outputs.
/// </summary>
public record A2ATaskArtifactUpdateEvent : A2AEvent
{
    public override string Kind => A2AEventKind.ArtifactUpdate;

    [JsonPropertyName("taskId")]
    public required string TaskId { get; init; }

    [JsonPropertyName("contextId")]
    public required string ContextId { get; init; }

    [JsonPropertyName("artifact")]
    public required A2AArtifact Artifact { get; init; }

    [JsonPropertyName("append")]
    public bool? Append { get; init; }

    [JsonPropertyName("lastChunk")]
    public bool? LastChunk { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// A2A Task status containing state and optional message.
/// </summary>
public record A2ATaskStatus
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    [JsonPropertyName("message")]
    public A2AMessage? Message { get; init; }
}

/// <summary>
/// Base type for A2A message parts.
/// Uses 'kind' discriminator for polymorphic deserialization.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(A2ATextPart), "text")]
[JsonDerivedType(typeof(A2ADataPart), "data")]
[JsonDerivedType(typeof(A2AFilePart), "file")]
public abstract record A2APart
{
    [JsonPropertyName("kind")]
    public abstract string Kind { get; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// A2A Text part for plain text content.
/// </summary>
public record A2ATextPart : A2APart
{
    public override string Kind => "text";

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>
/// A2A Data part for structured JSON data.
/// </summary>
public record A2ADataPart : A2APart
{
    public override string Kind => "data";

    [JsonPropertyName("data")]
    public required JsonElement Data { get; init; }
}

/// <summary>
/// A2A File part for file content.
/// </summary>
public record A2AFilePart : A2APart
{
    public override string Kind => "file";

    [JsonPropertyName("file")]
    public required A2AFile File { get; init; }
}

/// <summary>
/// A2A File content - either bytes or URI reference.
/// </summary>
public record A2AFile
{
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("bytes")]
    public string? Bytes { get; init; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// A2A Artifact representing generated output.
/// </summary>
public record A2AArtifact
{
    [JsonPropertyName("artifactId")]
    public required string ArtifactId { get; init; }

    [JsonPropertyName("parts")]
    public required List<A2APart> Parts { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// Input required metadata types for A2A input-required state.
/// </summary>
public static class A2AInputType
{
    public const string Question = "question";
    public const string PlanApproval = "plan-approval";
}

/// <summary>
/// Homespun-specific metadata for questions in input-required state.
/// </summary>
public record A2AInputRequiredMetadata
{
    [JsonPropertyName("inputType")]
    public required string InputType { get; init; }

    [JsonPropertyName("questions")]
    public List<A2AQuestionData>? Questions { get; init; }

    [JsonPropertyName("plan")]
    public string? Plan { get; init; }
}

/// <summary>
/// Question data for A2A input-required events.
/// </summary>
public record A2AQuestionData
{
    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("header")]
    public required string Header { get; init; }

    [JsonPropertyName("options")]
    public required List<A2AQuestionOption> Options { get; init; }

    [JsonPropertyName("multiSelect")]
    public required bool MultiSelect { get; init; }
}

/// <summary>
/// Question option for A2A questions.
/// </summary>
public record A2AQuestionOption
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>
/// Homespun-specific message metadata.
/// </summary>
public record A2AHomespunMessageMetadata
{
    [JsonPropertyName("sdkMessageType")]
    public string? SdkMessageType { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("toolUseId")]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("isThinking")]
    public bool? IsThinking { get; init; }

    [JsonPropertyName("isStreaming")]
    public bool? IsStreaming { get; init; }

    [JsonPropertyName("parentToolUseId")]
    public string? ParentToolUseId { get; init; }
}
