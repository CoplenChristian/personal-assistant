using System.Text;

namespace PersonalAssistant.Harness.Runtime;

public sealed record TerminalScreenSnapshot(string Data, int Columns, int Rows);

public static class TerminalScreenNormalizer
{
    private const int MaxScreenDataBytes = TerminalProtocol.MaxPayloadBytes / 2;

    public static TerminalScreenSnapshot Normalize(TmuxPaneSnapshot snapshot)
    {
        var normalized = snapshot.Data.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var data = LimitToLatestLines(normalized);
        var lines = data.Split('\n', StringSplitOptions.None);
        var columns = Math.Max(1, lines.Length == 0 ? 0 : lines.Max(line => line.Length));
        return new TerminalScreenSnapshot(data, columns, Math.Max(1, lines.Length));
    }

    private static string LimitToLatestLines(string data)
    {
        if (Encoding.UTF8.GetByteCount(data) <= MaxScreenDataBytes)
        {
            return data.TrimEnd('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(data);
        var start = bytes.Length - MaxScreenDataBytes;
        while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80)
        {
            start++;
        }

        var latest = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        var firstLineBreak = latest.IndexOf('\n');
        if (firstLineBreak >= 0 && firstLineBreak < latest.Length - 1)
        {
            latest = latest[(firstLineBreak + 1)..];
        }

        return latest.TrimEnd('\n');
    }
}
