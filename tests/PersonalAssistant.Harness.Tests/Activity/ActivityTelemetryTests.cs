using PersonalAssistant.Harness.Activity;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Activity;

public sealed class ActivityTelemetryTests
{
    [Fact]
    public void TryRecord_does_not_throw_when_the_sink_fails()
    {
        ActivityTelemetry.ResetForTests();
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
        Assert.True(ActivityTelemetry.RecordingDegraded);
        Assert.Equal(1, ActivityTelemetry.FailedRecordCount);
    }

    [Fact]
    public void Query_surfaces_audit_degraded_when_recording_has_failed()
    {
        ActivityTelemetry.ResetForTests();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        var database = new PersonalAssistant.Harness.Persistence.SqliteHarnessDatabase(connection);
        var service = new ActivityQueryService(database);
        ActivityTelemetry.TryRecord(new ThrowingActivitySink(), CreateEvent());

        var result = service.Query(new ActivityQueryRequest("2026-09-01", "UTC", null));

        Assert.True(result.AuditDegraded);
    }

    private static ActivityEvent CreateEvent() =>
        new(
            "event",
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            "personal",
            "personal",
            "agents",
            "start",
            "runtime-session",
            "success",
            null,
            """{"eventType":"test.event","outcome":"observed"}""");

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
