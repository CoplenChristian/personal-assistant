using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PersonalAssistant.Harness.Policies;

public static class RepositoryDefaultsLoader
{
    public static RuntimeDefaults Load(string path)
    {
        try
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var document = deserializer.Deserialize<RuntimeDefaultsDocument>(yaml)
                ?? throw new RepositoryDefaultsException("runtime.yaml is empty.");

            if (document.Version != 1)
            {
                throw new RepositoryDefaultsException("runtime.yaml must declare version 1.");
            }

            var defaults = new RuntimeDefaults(
                document.Version,
                document.Appearance.Theme,
                document.Appearance.BrowserScrollbackLines,
                document.Agents.Defaults.Runtime,
                document.Agents.Defaults.AutoStart,
                document.Tmux.HistoryLines,
                document.TerminalLogs.ActiveWarningBytes,
                document.TerminalLogs.RotatedFiles,
                document.NativeSessions.WarningBytes,
                document.NativeSessions.RotateBytes,
                document.NativeSessions.ArchiveTtlDays,
                document.Documents.AutomaticIndexing,
                document.Documents.AutomaticTocRegeneration,
                document.Memory.MaxFts5Results,
                document.Memory.AutoMaterializeGeneratedMemory,
                document.Scheduler.Timezone,
                document.Scheduler.MissedRunPolicy,
                document.Scheduler.MaxQueuedPromptsPerAgent,
                document.Safety.CheckpointBeforeRotation);

            ValidatePresenceAndShape(defaults);
            return defaults;
        }
        catch (RepositoryDefaultsException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RepositoryDefaultsException($"Unable to load runtime defaults: {exception.Message}");
        }
    }

    private static void ValidatePresenceAndShape(RuntimeDefaults defaults)
    {
        if (string.IsNullOrWhiteSpace(defaults.Theme)
            || string.IsNullOrWhiteSpace(defaults.DefaultRuntime)
            || string.IsNullOrWhiteSpace(defaults.AutomationTimezone)
            || string.IsNullOrWhiteSpace(defaults.MissedRunPolicy)
            || defaults.BrowserScrollbackLines <= 0
            || defaults.TmuxHistoryLines <= 0
            || defaults.TerminalLogWarningBytes <= 0
            || defaults.TerminalLogRotatedFiles <= 0
            || defaults.NativeSessionWarningBytes <= 0
            || defaults.NativeSessionRotateBytes <= 0
            || defaults.NativeSessionArchiveTtlDays <= 0
            || defaults.MaxFts5Results <= 0
            || defaults.MaxQueuedPromptsPerAgent < 0)
        {
            throw new RepositoryDefaultsException("runtime.yaml contains missing or non-positive required defaults.");
        }
    }

    private sealed class RuntimeDefaultsDocument
    {
        public int Version { get; set; }
        public AppearanceDefaults Appearance { get; set; } = new();
        public AgentsDefaults Agents { get; set; } = new();
        public TmuxDefaults Tmux { get; set; } = new();
        public TerminalLogsDefaults TerminalLogs { get; set; } = new();
        public NativeSessionsDefaults NativeSessions { get; set; } = new();
        public DocumentsDefaults Documents { get; set; } = new();
        public MemoryDefaults Memory { get; set; } = new();
        public SchedulerDefaults Scheduler { get; set; } = new();
        public SafetyDefaults Safety { get; set; } = new();
    }

    private sealed class AppearanceDefaults
    {
        public string Theme { get; set; } = string.Empty;
        public int BrowserScrollbackLines { get; set; }
    }

    private sealed class AgentsDefaults
    {
        public AgentDefaultValues Defaults { get; set; } = new();
    }

    private sealed class AgentDefaultValues
    {
        public string Runtime { get; set; } = string.Empty;
        public bool AutoStart { get; set; }
    }

    private sealed class TmuxDefaults { public int HistoryLines { get; set; } }

    private sealed class TerminalLogsDefaults
    {
        public long ActiveWarningBytes { get; set; }
        public int RotatedFiles { get; set; }
    }

    private sealed class NativeSessionsDefaults
    {
        public long WarningBytes { get; set; }
        public long RotateBytes { get; set; }
        public int ArchiveTtlDays { get; set; }
    }

    private sealed class DocumentsDefaults
    {
        public bool AutomaticIndexing { get; set; }
        public bool AutomaticTocRegeneration { get; set; }
    }

    private sealed class MemoryDefaults
    {
        public int MaxFts5Results { get; set; }
        public bool AutoMaterializeGeneratedMemory { get; set; }
    }

    private sealed class SchedulerDefaults
    {
        public string Timezone { get; set; } = string.Empty;
        public string MissedRunPolicy { get; set; } = string.Empty;
        public int MaxQueuedPromptsPerAgent { get; set; }
        public bool OneInjectedPromptPerAgent { get; set; }
        public bool ScheduledJobsTargetLogicalAgent { get; set; }
    }

    private sealed class SafetyDefaults { public bool CheckpointBeforeRotation { get; set; } }
}
