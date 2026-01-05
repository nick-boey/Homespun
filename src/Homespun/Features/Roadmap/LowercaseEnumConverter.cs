using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homespun.Features.Roadmap;

/// <summary>
/// JSON converter that serializes enums to lowercase strings.
/// For example, ChangeType.Feature becomes "feature" instead of "Feature".
/// </summary>
public class LowercaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            throw new JsonException($"Cannot convert empty string to {typeof(T).Name}");
        }

        // Try parsing case-insensitively
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
        {
            return result;
        }

        throw new JsonException($"Cannot convert '{value}' to {typeof(T).Name}");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
    }
}

/// <summary>
/// JSON converter for FutureChangeStatus with camelCase serialization.
/// Pending -> "pending", InProgress -> "inProgress", etc.
/// </summary>
public class FutureChangeStatusConverter : JsonConverter<FutureChangeStatus>
{
    public override FutureChangeStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return FutureChangeStatus.Pending; // Default
        }

        return value.ToLowerInvariant() switch
        {
            "pending" => FutureChangeStatus.Pending,
            "inprogress" => FutureChangeStatus.InProgress,
            "awaitingpr" => FutureChangeStatus.AwaitingPR,
            "complete" => FutureChangeStatus.Complete,
            _ => throw new JsonException($"Cannot convert '{value}' to FutureChangeStatus")
        };
    }

    public override void Write(Utf8JsonWriter writer, FutureChangeStatus value, JsonSerializerOptions options)
    {
        var stringValue = value switch
        {
            FutureChangeStatus.Pending => "pending",
            FutureChangeStatus.InProgress => "inProgress",
            FutureChangeStatus.AwaitingPR => "awaitingPR",
            FutureChangeStatus.Complete => "complete",
            _ => value.ToString().ToLowerInvariant()
        };
        writer.WriteStringValue(stringValue);
    }
}
