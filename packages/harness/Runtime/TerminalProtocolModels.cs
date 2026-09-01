using System.Text;
using System.Text.Json;

namespace PersonalAssistant.Harness.Runtime;

public static class TerminalProtocol
{
    public const string ContractVersion = "phase-0c-terminal.v1";
    public const int MaxPayloadBytes = 64 * 1024;
    public const int MaxColumns = 500;
    public const int MaxRows = 250;
}

public interface ITerminalClientFrame
{
    string Type { get; }
}

public sealed record TerminalInputFrame(long Sequence, string Data) : ITerminalClientFrame
{
    public string Type => "input";
}

public sealed record TerminalResizeFrame(int Columns, int Rows) : ITerminalClientFrame
{
    public string Type => "resize";
}

public sealed record TerminalPingFrame(long Sequence) : ITerminalClientFrame
{
    public string Type => "ping";
}

public sealed record TerminalHelloFrame(string Protocol, string AgentId)
{
    public string Type => "hello";
}

public sealed record TerminalSnapshotFrame(long Sequence, string Data, int ScrollbackLines)
{
    public string Type => "snapshot";
    public bool HydrationBoundary => true;
}

public sealed record TerminalOutputFrame(long Sequence, string Data)
{
    public string Type => "output";
}

public sealed record TerminalStateFrame(string State)
{
    public string Type => "state";
}

public sealed record TerminalInputAcknowledgementFrame(long Sequence)
{
    public string Type => "inputAck";
}

public sealed record TerminalPongFrame(long Sequence)
{
    public string Type => "pong";
}

public sealed record TerminalErrorFrame(string Code, string? Detail = null)
{
    public string Type => "error";
}

public sealed class TerminalProtocolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class TerminalProtocolValidator
{
    public static ITerminalClientFrame ParseClientFrame(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeProperty))
            {
                throw Invalid("invalid_frame", "A terminal frame requires a type.");
            }

            var type = typeProperty.GetString();
            return type switch
            {
                "input" => ParseInput(root),
                "resize" => ParseResize(root),
                "ping" => ParsePing(root),
                _ => throw Invalid("unknown_frame_type", "The terminal frame type is not supported.")
            };
        }
        catch (TerminalProtocolException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Invalid("invalid_frame", $"The terminal frame is not valid JSON: {exception.Message}");
        }
    }

    public static void ValidateHello(TerminalHelloFrame frame, string expectedAgentId)
    {
        if (!string.Equals(frame.Protocol, TerminalProtocol.ContractVersion, StringComparison.Ordinal))
        {
            throw Invalid("unsupported_protocol", "The terminal protocol version is not supported.");
        }

        if (!string.Equals(frame.AgentId, expectedAgentId, StringComparison.Ordinal))
        {
            throw Invalid("agent_binding_invalid", "The terminal frame is bound to a different agent.");
        }
    }

    public static void ValidateOutputSequence(long previousSequence, long nextSequence)
    {
        if (nextSequence <= previousSequence)
        {
            throw Invalid("sequence_not_monotonic", "Terminal output sequence numbers must increase.");
        }
    }

    public static void ValidatePayload(string data)
    {
        if (Encoding.UTF8.GetByteCount(data) > TerminalProtocol.MaxPayloadBytes)
        {
            throw Invalid("payload_too_large", "The terminal payload exceeds the configured limit.");
        }
    }

    public static void ValidateResize(TerminalResizeFrame frame)
    {
        if (frame.Columns is < 1 or > TerminalProtocol.MaxColumns || frame.Rows is < 1 or > TerminalProtocol.MaxRows)
        {
            throw Invalid("resize_invalid", "Terminal dimensions are outside the supported bounds.");
        }
    }

    private static TerminalInputFrame ParseInput(JsonElement root)
    {
        var sequence = ReadInt64(root, "sequence");
        var data = ReadString(root, "data");
        ValidatePayload(data);
        return new TerminalInputFrame(sequence, data);
    }

    private static TerminalResizeFrame ParseResize(JsonElement root)
    {
        var frame = new TerminalResizeFrame(
            ReadInt32(root, "columns"),
            ReadInt32(root, "rows"));
        ValidateResize(frame);
        return frame;
    }

    private static TerminalPingFrame ParsePing(JsonElement root) =>
        new(ReadInt64(root, "sequence"));

    private static string ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw Invalid("invalid_frame", $"Terminal frame property {propertyName} must be a string.");
        }

        return property.GetString() ?? throw Invalid("invalid_frame", $"Terminal frame property {propertyName} is required.");
    }

    private static long ReadInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || !property.TryGetInt64(out var value) || value < 0)
        {
            throw Invalid("invalid_frame", $"Terminal frame property {propertyName} must be a non-negative integer.");
        }

        return value;
    }

    private static int ReadInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value))
        {
            throw Invalid("invalid_frame", $"Terminal frame property {propertyName} must be an integer.");
        }

        return value;
    }

    private static TerminalProtocolException Invalid(string code, string message) =>
        new(code, message);
}
