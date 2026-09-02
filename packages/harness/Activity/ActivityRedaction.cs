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
            activityEvent.Target,
            activityEvent.Status,
            activityEvent.DurationMs,
            RedactMetadata(activityEvent.MetadataJson));
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
                    if (SensitiveMetadataKeys.Contains(property.Name))
                    {
                        writer.WriteString(property.Name, "[redacted]");
                        continue;
                    }

                    if (property.Value.ValueKind is JsonValueKind.String)
                    {
                        var value = property.Value.GetString() ?? string.Empty;
                        writer.WriteString(property.Name, SensitivePathPattern.IsMatch(value) ? "[redacted]" : value);
                        continue;
                    }

                    property.WriteTo(writer);
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
