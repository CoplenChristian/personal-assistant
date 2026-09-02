namespace PersonalAssistant.Harness.Activity;

public static class ActivityCategoryKeys
{
    public const string PromptsDelivered = "promptsDelivered";
    public const string ScheduledRuns = "scheduledRuns";
    public const string ScheduledPromptsQueued = "scheduledPromptsQueued";
    public const string ScheduledPromptsDropped = "scheduledPromptsDropped";
    public const string EmailReads = "emailReads";
    public const string EmailModifications = "emailModifications";
    public const string MessagesSent = "messagesSent";
    public const string MessagesReplied = "messagesReplied";
    public const string MessagesBlocked = "messagesBlocked";
    public const string CalendarWrites = "calendarWrites";
    public const string ReminderWrites = "reminderWrites";
    public const string MemoryWrites = "memoryWrites";
    public const string MemoryCheckpoints = "memoryCheckpoints";
    public const string DocumentIndexing = "documentIndexing";
    public const string BrowserActions = "browserActions";
    public const string SecurityBlocked = "securityBlocked";
    public const string Failures = "failures";
    public const string AgentStarts = "agentStarts";
    public const string AgentStops = "agentStops";
    public const string AgentClears = "agentClears";
    public const string AgentRotations = "agentRotations";
    public const string RosterChanges = "rosterChanges";

    public static IReadOnlyList<string> All { get; } = [
        PromptsDelivered,
        ScheduledRuns,
        ScheduledPromptsQueued,
        ScheduledPromptsDropped,
        EmailReads,
        EmailModifications,
        MessagesSent,
        MessagesReplied,
        MessagesBlocked,
        CalendarWrites,
        ReminderWrites,
        MemoryWrites,
        MemoryCheckpoints,
        DocumentIndexing,
        BrowserActions,
        SecurityBlocked,
        Failures,
        AgentStarts,
        AgentStops,
        AgentClears,
        AgentRotations,
        RosterChanges
    ];
}
