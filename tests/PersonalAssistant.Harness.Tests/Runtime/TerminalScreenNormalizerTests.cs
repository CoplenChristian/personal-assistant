using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class TerminalScreenNormalizerTests
{
    [Fact]
    public void Normalize_converts_terminal_line_endings_and_reports_screen_dimensions()
    {
        var screen = TerminalScreenNormalizer.Normalize(new TmuxPaneSnapshot("short\r\nlonger\r\n", 5000));

        Assert.Equal("short\nlonger", screen.Data);
        Assert.Equal(6, screen.Columns);
        Assert.Equal(2, screen.Rows);
    }

    [Fact]
    public void Normalize_keeps_the_latest_content_when_capture_exceeds_the_frame_budget()
    {
        var screen = TerminalScreenNormalizer.Normalize(new TmuxPaneSnapshot(
            new string('x', TerminalProtocol.MaxPayloadBytes) + "\nlatest",
            5000));

        Assert.Equal("latest", screen.Data);
        Assert.Equal(6, screen.Columns);
        Assert.Equal(1, screen.Rows);
    }
}
