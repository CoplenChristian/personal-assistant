using Microsoft.Data.Sqlite;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Persistence;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Activity;

public sealed class ActivityQueryServiceTests
{
    [Fact]
    public void Query_returns_every_counter_category_with_zero_values_when_no_events_exist()
    {
        using var fixture = CreateFixture();
        var result = fixture.Service.Query(new ActivityQueryRequest("2026-09-01", "UTC", null));

        Assert.Equal(ActivityQueryService.ContractVersion, result.ContractVersion);
        Assert.Equal("2026-09-01", result.Date);
        Assert.Equal("UTC", result.Timezone);
        Assert.Equal(ActivityCategoryKeys.All.Count, result.Counters.Count);
        Assert.All(ActivityCategoryKeys.All, key => Assert.Equal(0, result.Counters[key]));
        Assert.Empty(result.RecentEvents);
    }

    [Fact]
    public void Query_aggregates_counters_and_orders_recent_events_deterministically()
    {
        using var fixture = CreateFixture();
        var sink = new SqliteActivityEventSink(fixture.Database);
        sink.Append(CreateEvent(
            "agent-start",
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            "agents",
            "start",
            "success"));
        sink.Append(CreateEvent(
            "memory-checkpoint",
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            "memory",
            "checkpoint",
            "success"));
        sink.Append(CreateEvent(
            "agent-clear-blocked",
            new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.Zero),
            "agents",
            "clear",
            "blocked"));
        sink.Append(CreateEvent(
            "older",
            new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            "sessions",
            "terminal_hydration",
            "success"));
        sink.Append(CreateEvent(
            "newer",
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            "sessions",
            "terminal_input",
            "success"));

        var result = fixture.Service.Query(new ActivityQueryRequest("2026-09-01", "UTC", 2));

        Assert.Equal(1, result.Counters[ActivityCategoryKeys.AgentStarts]);
        Assert.Equal(1, result.Counters[ActivityCategoryKeys.MemoryCheckpoints]);
        Assert.Equal(1, result.Counters[ActivityCategoryKeys.AgentClears]);
        Assert.Equal(1, result.Counters[ActivityCategoryKeys.SecurityBlocked]);
        Assert.Equal(2, result.RecentEvents.Count);
        Assert.Equal("newer", result.RecentEvents[0].Id);
        Assert.Equal("agent-clear-blocked", result.RecentEvents[1].Id);
    }

    [Fact]
    public void Query_respects_timezone_day_boundaries()
    {
        using var fixture = CreateFixture();
        var sink = new SqliteActivityEventSink(fixture.Database);
        sink.Append(CreateEvent(
            "late-night",
            new DateTimeOffset(2026, 9, 1, 23, 30, 0, TimeSpan.Zero),
            "agents",
            "start",
            "success"));
        sink.Append(CreateEvent(
            "next-day",
            new DateTimeOffset(2026, 9, 2, 0, 30, 0, TimeSpan.Zero),
            "agents",
            "start",
            "success"));

        var firstDay = fixture.Service.Query(new ActivityQueryRequest("2026-09-01", "UTC", null));
        var secondDay = fixture.Service.Query(new ActivityQueryRequest("2026-09-02", "UTC", null));

        Assert.Equal(1, firstDay.Counters[ActivityCategoryKeys.AgentStarts]);
        Assert.Single(firstDay.RecentEvents);
        Assert.Equal("late-night", firstDay.RecentEvents[0].Id);
        Assert.Equal(1, secondDay.Counters[ActivityCategoryKeys.AgentStarts]);
        Assert.Single(secondDay.RecentEvents);
        Assert.Equal("next-day", secondDay.RecentEvents[0].Id);
    }

    [Fact]
    public void Query_rejects_invalid_dates_timezones_and_feed_limits()
    {
        using var fixture = CreateFixture();

        var invalidDate = Assert.Throws<ActivityQueryException>(() =>
            fixture.Service.Query(new ActivityQueryRequest("09-01-2026", "UTC", null)));
        Assert.Equal("activity_date_invalid", invalidDate.Code);

        var invalidTimezone = Assert.Throws<ActivityQueryException>(() =>
            fixture.Service.Query(new ActivityQueryRequest("2026-09-01", "Not/AZone", null)));
        Assert.Equal("activity_timezone_invalid", invalidTimezone.Code);

        var invalidLimit = Assert.Throws<ActivityQueryException>(() =>
            fixture.Service.Query(new ActivityQueryRequest("2026-09-01", "UTC", 500)));
        Assert.Equal("activity_feed_limit_invalid", invalidLimit.Code);
    }

    [Fact]
    public void Query_does_not_mutate_existing_events_on_refresh()
    {
        using var fixture = CreateFixture();
        var sink = new SqliteActivityEventSink(fixture.Database);
        sink.Append(CreateEvent(
            "stable",
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            "agents",
            "start",
            "success"));
        var before = fixture.Database.ReadActivityEvents();

        fixture.Service.Query(new ActivityQueryRequest("2026-09-01", "UTC", null));
        fixture.Service.Query(new ActivityQueryRequest("2026-09-01", "UTC", null));

        var after = fixture.Database.ReadActivityEvents();
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before[0].Id, after[0].Id);
        Assert.Equal(before[0].MetadataJson, after[0].MetadataJson);
    }

    [Fact]
    public void Redaction_strips_sensitive_metadata_and_malformed_json()
    {
        var sensitive = ActivityRedaction.RedactMetadata("""
            {
              "eventType": "terminal.input",
              "input": "secret keystrokes",
              "path": "/Users/me/runtime/agents/personal/terminal/active.log",
              "token": "abc123"
            }
            """);

        Assert.Contains("[redacted]", sensitive, StringComparison.Ordinal);
        Assert.DoesNotContain("secret keystrokes", sensitive, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/me", sensitive, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", sensitive, StringComparison.Ordinal);

        Assert.Equal("{}", ActivityRedaction.RedactMetadata("{not-json"));
    }

    private static ActivityFixture CreateFixture()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        var database = new SqliteHarnessDatabase(connection);
        return new ActivityFixture(database, new ActivityQueryService(database));
    }

    private static ActivityEvent CreateEvent(
        string id,
        DateTimeOffset timestamp,
        string category,
        string operation,
        string status) =>
        new(
            id,
            timestamp,
            "personal",
            "personal",
            category,
            operation,
            "runtime-session",
            status,
            null,
            """{"eventType":"test.event","outcome":"observed"}""");

    private sealed class ActivityFixture : IDisposable
    {
        public ActivityFixture(SqliteHarnessDatabase database, ActivityQueryService service)
        {
            Database = database;
            Service = service;
        }

        public SqliteHarnessDatabase Database { get; }
        public ActivityQueryService Service { get; }

        public void Dispose() => Database.Dispose();
    }
}
