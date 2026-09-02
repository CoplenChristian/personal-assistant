using PersonalAssistant.Harness.Activity;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Activity;

public sealed class ActivityCounterAggregatorTests
{
    [Fact]
    public void Aggregate_counts_security_blocked_only_for_explicit_security_events()
    {
        var counters = ActivityCounterAggregator.Aggregate([
            Event("agents", "clear", "blocked"),
            Event("security", "block", "blocked"),
            Event("agents", "rotate", "failure"),
        ]);

        Assert.Equal(1, counters[ActivityCategoryKeys.AgentClears]);
        Assert.Equal(1, counters[ActivityCategoryKeys.SecurityBlocked]);
        Assert.Equal(1, counters[ActivityCategoryKeys.AgentRotations]);
        Assert.Equal(1, counters[ActivityCategoryKeys.Failures]);
    }

    [Fact]
    public void Aggregate_maps_every_counter_category()
    {
        var counters = ActivityCounterAggregator.Aggregate([
            Event("prompts", "deliver", "success"),
            Event("scheduler", "run", "success"),
            Event("scheduler", "queue", "success"),
            Event("scheduler", "drop", "success"),
            Event("email", "read", "success"),
            Event("email", "modify", "success"),
            Event("messages", "send", "success"),
            Event("messages", "reply", "success"),
            Event("messages", "block", "success"),
            Event("calendar", "write", "success"),
            Event("reminders", "write", "success"),
            Event("memory", "write", "success"),
            Event("memory", "checkpoint", "success"),
            Event("documents", "index", "success"),
            Event("browser", "open", "success"),
            Event("agents", "start", "success"),
            Event("agents", "stop", "success"),
            Event("agents", "clear", "success"),
            Event("agents", "rotate", "success"),
            Event("agents", "roster_changed", "success"),
        ]);

        Assert.Equal(1, counters[ActivityCategoryKeys.PromptsDelivered]);
        Assert.Equal(1, counters[ActivityCategoryKeys.ScheduledRuns]);
        Assert.Equal(1, counters[ActivityCategoryKeys.ScheduledPromptsQueued]);
        Assert.Equal(1, counters[ActivityCategoryKeys.ScheduledPromptsDropped]);
        Assert.Equal(1, counters[ActivityCategoryKeys.EmailReads]);
        Assert.Equal(1, counters[ActivityCategoryKeys.EmailModifications]);
        Assert.Equal(1, counters[ActivityCategoryKeys.MessagesSent]);
        Assert.Equal(1, counters[ActivityCategoryKeys.MessagesReplied]);
        Assert.Equal(1, counters[ActivityCategoryKeys.MessagesBlocked]);
        Assert.Equal(1, counters[ActivityCategoryKeys.CalendarWrites]);
        Assert.Equal(1, counters[ActivityCategoryKeys.ReminderWrites]);
        Assert.Equal(1, counters[ActivityCategoryKeys.MemoryWrites]);
        Assert.Equal(1, counters[ActivityCategoryKeys.MemoryCheckpoints]);
        Assert.Equal(1, counters[ActivityCategoryKeys.DocumentIndexing]);
        Assert.Equal(1, counters[ActivityCategoryKeys.BrowserActions]);
        Assert.Equal(1, counters[ActivityCategoryKeys.AgentStarts]);
        Assert.Equal(1, counters[ActivityCategoryKeys.AgentStops]);
        Assert.Equal(1, counters[ActivityCategoryKeys.AgentClears]);
        Assert.Equal(1, counters[ActivityCategoryKeys.AgentRotations]);
        Assert.Equal(1, counters[ActivityCategoryKeys.RosterChanges]);
    }

    private static ActivityEvent Event(string category, string operation, string status) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            "personal",
            "personal",
            category,
            operation,
            "runtime-session",
            status,
            null,
            """{"eventType":"test.event","outcome":"observed"}""");
}
