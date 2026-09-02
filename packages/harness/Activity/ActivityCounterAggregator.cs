namespace PersonalAssistant.Harness.Activity;

public static class ActivityCounterAggregator
{
    public static Dictionary<string, int> CreateEmptyCounters()
    {
        return ActivityCategoryKeys.All.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, int> Aggregate(IReadOnlyList<ActivityEvent> events)
    {
        var counters = CreateEmptyCounters();
        foreach (var activityEvent in events)
        {
            foreach (var key in MapEventToCounterKeys(activityEvent))
            {
                counters[key]++;
            }
        }

        return counters;
    }

    private static IEnumerable<string> MapEventToCounterKeys(ActivityEvent activityEvent)
    {
        if (string.Equals(activityEvent.Status, "failure", StringComparison.OrdinalIgnoreCase)
            || string.Equals(activityEvent.Status, "error", StringComparison.OrdinalIgnoreCase))
        {
            yield return ActivityCategoryKeys.Failures;
        }

        switch (activityEvent.Category, activityEvent.Operation)
        {
            case ("prompts", "deliver"):
            case ("prompts", "delivered"):
                yield return ActivityCategoryKeys.PromptsDelivered;
                break;
            case ("scheduler", "run"):
                yield return ActivityCategoryKeys.ScheduledRuns;
                break;
            case ("scheduler", "queue"):
                yield return ActivityCategoryKeys.ScheduledPromptsQueued;
                break;
            case ("scheduler", "drop"):
                yield return ActivityCategoryKeys.ScheduledPromptsDropped;
                break;
            case ("email", "read"):
                yield return ActivityCategoryKeys.EmailReads;
                break;
            case ("email", "modify"):
                yield return ActivityCategoryKeys.EmailModifications;
                break;
            case ("messages", "send"):
                yield return ActivityCategoryKeys.MessagesSent;
                break;
            case ("messages", "reply"):
                yield return ActivityCategoryKeys.MessagesReplied;
                break;
            case ("messages", "block"):
                yield return ActivityCategoryKeys.MessagesBlocked;
                break;
            case ("calendar", "write"):
                yield return ActivityCategoryKeys.CalendarWrites;
                break;
            case ("reminders", "write"):
                yield return ActivityCategoryKeys.ReminderWrites;
                break;
            case ("memory", "write"):
                yield return ActivityCategoryKeys.MemoryWrites;
                break;
            case ("memory", "checkpoint"):
                yield return ActivityCategoryKeys.MemoryCheckpoints;
                break;
            case ("documents", "index"):
                yield return ActivityCategoryKeys.DocumentIndexing;
                break;
            case ("browser", _):
                yield return ActivityCategoryKeys.BrowserActions;
                break;
            case ("security", "block"):
                yield return ActivityCategoryKeys.SecurityBlocked;
                break;
            case ("agents", "start"):
                yield return ActivityCategoryKeys.AgentStarts;
                break;
            case ("agents", "stop"):
                yield return ActivityCategoryKeys.AgentStops;
                break;
            case ("agents", "clear"):
                yield return ActivityCategoryKeys.AgentClears;
                break;
            case ("agents", "rotate"):
                yield return ActivityCategoryKeys.AgentRotations;
                break;
            case ("agents", "roster_changed"):
            case ("agents", "roster_change"):
                yield return ActivityCategoryKeys.RosterChanges;
                break;
        }
    }
}
