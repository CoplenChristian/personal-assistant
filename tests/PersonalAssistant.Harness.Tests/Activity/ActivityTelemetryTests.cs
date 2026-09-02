using PersonalAssistant.Harness.Activity;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Activity;

public sealed class ActivityTelemetryTests
{
    [Fact]
    public void TryRecord_does_not_throw_when_the_sink_fails()
    {
        var sink = new ThrowingActivitySink();
        var exception = Record.Exception(() =>
            ActivityTelemetry.TryRecord(sink, new ActivityEvent(
                "event",
                DateTimeOffset.UtcNow,
                "personal",
                "personal",
                "sessions",
                "terminal_input",
                "runtime-terminal",
                "success",
                null,
                """{"eventType":"terminal.input","outcome":"accepted"}""")));

        Assert.Null(exception);
        Assert.Equal(1, sink.Attempts);
    }

    private sealed class ThrowingActivitySink : IActivityEventSink
    {
        public int Attempts { get; private set; }

        public void Append(ActivityEvent activityEvent)
        {
            Attempts++;
            throw new InvalidOperationException("telemetry unavailable");
        }
    }
}
