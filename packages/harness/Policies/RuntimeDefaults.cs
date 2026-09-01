namespace PersonalAssistant.Harness.Policies;

public sealed record RuntimeDefaults(
    int Version,
    string Theme,
    int BrowserScrollbackLines,
    string DefaultRuntime,
    bool DefaultAutoStart,
    int TmuxHistoryLines,
    long TerminalLogWarningBytes,
    int TerminalLogRotatedFiles,
    long NativeSessionWarningBytes,
    long NativeSessionRotateBytes,
    int NativeSessionArchiveTtlDays,
    bool AutomaticIndexing,
    bool AutomaticTocRegeneration,
    int MaxFts5Results,
    bool AutoMaterializeGeneratedMemory,
    string AutomationTimezone,
    string MissedRunPolicy,
    int MaxQueuedPromptsPerAgent,
    bool CheckpointBeforeRotation);

public sealed class RepositoryDefaultsException(string message) : Exception(message);
