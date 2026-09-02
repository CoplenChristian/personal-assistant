using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonalAssistant.Harness.Activity;

public static class ActivityRedaction
{
    private static readonly Regex SensitivePathPattern = new(
        @"(?i)(/Users/|/home/|runtime/|vault/|\.ssh/|keychain|transcript|MEMORY\.md|HANDOFF\.md)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SensitiveMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "token",
        "secret",
        "credential",
        "apiKey",
        "input",
        "data",
        "transcript",
        "content",
        "output",
        "screen",
        "path",
        "generatedMemory",
        "generatedHandoff",
        "checkpointPath",
        "byteCount",
        "hash"
    };

    private static readonly HashSet<string> AllowedMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "eventType",
        "outcome",
        "state",
        "errorCode",
        "desiredState",
        "observedState",
        "adopted",
        "resumeAttempted",
        "resumeFallback",
        "scope",
        "requiresRestart",
        "keys",
        "agentId"
    };

    public static ActivityPublicEvent ToPublicEvent(ActivityEvent activityEvent)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);
        return new ActivityPublicEvent(
            activityEvent.Id,
            activityEvent.Timestamp,
            activityEvent.AgentId,
            activityEvent.Realm,
            activityEvent.Category,
            activityEvent.Operation,
            RedactTarget(activityEvent.Target),
            activityEvent.Status,
            activityEvent.DurationMs,
            RedactMetadata(activityEvent.MetadataJson));
    }

    public static string? RedactTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return target;
        }

        return SensitivePathPattern.IsMatch(target) ? "[redacted]" : target;
    }

    public static string RedactMetadata(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return "{}";
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteRedactedValue(writer, property.Value, property.Name);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private static void WriteRedactedValue(Utf8JsonWriter writer, JsonElement value, string propertyName)
    {
        if (SensitiveMetadataKeys.Contains(propertyName)
            || !AllowedMetadataKeys.Contains(propertyName))
        {
            writer.WriteStringValue("[redacted]");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteRedactedValue(writer, property.Value, property.Name);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteRedactedValue(writer, item, propertyName);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                writer.WriteStringValue(SensitivePathPattern.IsMatch(text) ? "[redacted]" : text);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}

public sealed record ActivityPublicEvent(
    string Id,
    DateTimeOffset Timestamp,
    string? AgentId,
    string? Realm,
    string Category,
    string Operation,
    string? Target,
    string Status,
    long? DurationMs,
    string MetadataJson);
