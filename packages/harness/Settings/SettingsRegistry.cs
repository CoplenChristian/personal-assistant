using System.Globalization;
using PersonalAssistant.Harness.Bootstrap;

namespace PersonalAssistant.Harness.Settings;

public sealed class SettingsRegistry
{
    private readonly Dictionary<string, SettingDefinition> definitions;

    public SettingsRegistry(IEnumerable<SettingDefinition> definitions)
    {
        this.definitions = definitions.ToDictionary(definition => definition.Key, StringComparer.Ordinal);
    }

    public IReadOnlyList<SettingDefinition> Definitions => definitions.Values.ToArray();

    public bool TryGet(string key, out SettingDefinition? definition) => definitions.TryGetValue(key, out definition);

    public SettingDefinition Get(string key) =>
        TryGet(key, out var definition)
            ? definition!
            : throw new SettingsException("unknown_setting", $"Unknown setting: {key}.");

    public IReadOnlyDictionary<string, object?> ResolveDefaults(SettingsContext context) =>
        definitions.Values.ToDictionary(definition => definition.Key, definition => definition.DefaultResolver(context), StringComparer.Ordinal);

    public void ValidateCandidate(IReadOnlyDictionary<string, object?> values, SettingsContext context)
    {
        foreach (var definition in definitions.Values)
        {
            if (!values.TryGetValue(definition.Key, out var value))
            {
                throw new SettingsStoreException($"Missing effective value for {definition.Key}.");
            }

            var error = definition.Validate(value, context);
            if (error is not null)
            {
                throw new SettingsException("invalid_value", $"{definition.Key} {error}.", new Dictionary<string, string>
                {
                    [definition.Key] = error
                });
            }
        }

        var warning = GetInt64(values, "sessions.nativeSessionWarningBytes");
        var rotate = GetInt64(values, "sessions.nativeSessionRotateBytes");
        if (rotate <= warning)
        {
            throw new SettingsException(
                "cross_setting_invalid",
                "Native session hard-rotate size must be greater than its warning size.",
                new Dictionary<string, string>
                {
                    ["sessions.nativeSessionRotateBytes"] = "must be greater than sessions.nativeSessionWarningBytes"
                });
        }

        var theme = GetString(values, "appearance.theme");
        if (theme is not ("system" or "light" or "dark"))
        {
            throw new SettingsException("invalid_value", "Theme must be system, light, or dark.");
        }

        var runtime = GetString(values, "agents.defaults.runtime");
        if (runtime is not ("claude" or "codex"))
        {
            throw new SettingsException("invalid_value", "Default runtime must be claude or codex.");
        }

        var missedPolicy = GetString(values, "automation.missedRunPolicy");
        if (missedPolicy is not ("skip" or "run-once"))
        {
            throw new SettingsException("invalid_value", "Missed-run policy must be skip or run-once.");
        }

        var timezone = GetString(values, "automation.timezone");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new SettingsException("invalid_value", "Automation timezone is not recognized.", new Dictionary<string, string>
            {
                ["automation.timezone"] = "must be a valid system timezone"
            });
        }
        catch (InvalidTimeZoneException)
        {
            throw new SettingsException("invalid_value", "Automation timezone is invalid.", new Dictionary<string, string>
            {
                ["automation.timezone"] = "must be a valid system timezone"
            });
        }

        var vaultPath = GetString(values, "documents.vaultPath");
        if (BootstrapResolver.IsWithin(context.RepositoryRoot, vaultPath))
        {
            throw new SettingsException("invalid_value", "Document vault path cannot be inside the source repository.", new Dictionary<string, string>
            {
                ["documents.vaultPath"] = "must be outside the source repository"
            });
        }
    }

    public static SettingsRegistry CreateDefault()
    {
        static SettingDefinition StringSetting(
            string key,
            string category,
            string label,
            string description,
            string source,
            Func<SettingsContext, object?> defaultResolver,
            bool editable = true,
            bool resettable = true,
            bool restart = false,
            bool bootstrap = false,
            SettingConstraints? constraints = null,
            Func<object?, SettingsContext, string?>? validator = null,
            Func<object?, SettingsContext, object?>? normalizer = null) => new(
                key, category, label, description, SettingValueType.String, [], SettingScopeType.Global,
                editable, resettable, restart, bootstrap, false, constraints ?? new(), source, defaultResolver,
                SettingDefinition.ParseString, validator, normalizer);

        static SettingDefinition IntegerSetting(
            string key,
            string category,
            string label,
            string description,
            string source,
            Func<SettingsContext, object?> defaultResolver,
            long minimum,
            long maximum,
            bool restart = false,
            bool editable = true,
            bool resettable = true,
            bool bootstrap = false,
            string? unit = null) => new(
                key, category, label, description, SettingValueType.Integer, [], SettingScopeType.Global,
                editable, resettable, restart, bootstrap, false, new SettingConstraints(minimum, maximum, Unit: unit), source, defaultResolver,
                SettingDefinition.ParseInteger);

        static SettingDefinition BooleanSetting(
            string key,
            string category,
            string label,
            string description,
            string source,
            Func<SettingsContext, object?> defaultResolver,
            bool restart = false) => new(
                key, category, label, description, SettingValueType.Boolean, [], SettingScopeType.Global,
                true, true, restart, false, false, new(), source, defaultResolver, SettingDefinition.ParseBoolean);

        static SettingDefinition EnumSetting(
            string key,
            string category,
            string label,
            string description,
            string source,
            IReadOnlyList<string> options,
            Func<SettingsContext, object?> defaultResolver,
            bool restart = false) => new(
                key, category, label, description, SettingValueType.Enum, options, SettingScopeType.Global,
                true, true, restart, false, false, new SettingConstraints(Options: options), source, defaultResolver,
                SettingDefinition.ParseEnum(options));

        static SettingDefinition LockedStatus(
            string key,
            string label,
            string source,
            string reason,
            Func<SettingsContext, object?> defaultResolver) => new(
                key, "Safety", label, reason, SettingValueType.Status, [], SettingScopeType.Global,
                false, false, false, false, false, new(), source, defaultResolver,
                SettingDefinition.ParseString);

        return new SettingsRegistry(
        [
            EnumSetting("appearance.theme", "General", "Theme", "Color scheme used by the local dashboard.", "repo-default", ["system", "light", "dark"], c => c.Defaults.Theme),
            IntegerSetting("appearance.browserScrollbackLines", "General", "Browser terminal scrollback", "Terminal history shown in the browser.", "repo-default", c => (long)c.Defaults.BrowserScrollbackLines, 100, 100000, unit: "lines"),
            EnumSetting("agents.defaults.runtime", "Agents", "Default runtime", "Runtime used when creating a new agent.", "repo-default", ["claude", "codex"], c => c.Defaults.DefaultRuntime),
            BooleanSetting("agents.defaults.autoStart", "Agents", "Auto-start new agents", "Start newly created agents automatically.", "repo-default", c => c.Defaults.DefaultAutoStart),
            IntegerSetting("sessions.tmuxHistoryLines", "Sessions", "tmux history lines", "Scrollback retained by tmux sessions.", "repo-default", c => (long)c.Defaults.TmuxHistoryLines, 100, 100000, true, unit: "lines"),
            IntegerSetting("sessions.terminalLogWarningBytes", "Sessions", "Terminal log warning", "Warn when an active terminal log reaches this size.", "repo-default", c => c.Defaults.TerminalLogWarningBytes, 1, 1073741824, true, unit: "bytes"),
            IntegerSetting("sessions.terminalLogRotatedFiles", "Sessions", "Rotated terminal logs", "Number of rotated terminal logs to retain.", "repo-default", c => (long)c.Defaults.TerminalLogRotatedFiles, 1, 100, true, unit: "files"),
            IntegerSetting("sessions.nativeSessionWarningBytes", "Sessions", "Native session warning", "Warn before a native session reaches this size.", "repo-default", c => c.Defaults.NativeSessionWarningBytes, 1, 1073741824, true, unit: "bytes"),
            IntegerSetting("sessions.nativeSessionRotateBytes", "Sessions", "Native session hard rotate", "Rotate a native session at this size.", "repo-default", c => c.Defaults.NativeSessionRotateBytes, 1, 4294967296, true, unit: "bytes"),
            IntegerSetting("sessions.nativeSessionArchiveTtlDays", "Sessions", "Native session archive TTL", "Days to retain archived native session artifacts.", "repo-default", c => (long)c.Defaults.NativeSessionArchiveTtlDays, 1, 3650, true, unit: "days"),
            StringSetting("documents.vaultPath", "Documents & Memory", "Personal document vault", "External path indexed in a later document phase.", "environment", c => c.Bootstrap.VaultDefaultPath, true, true, true, false, new SettingConstraints(Format: "path"), ValidatePath, NormalizePath),
            BooleanSetting("documents.automaticIndexing", "Documents & Memory", "Automatic document indexing", "Watch the external vault for future indexing.", "repo-default", c => c.Defaults.AutomaticIndexing, true),
            BooleanSetting("documents.automaticTocRegeneration", "Documents & Memory", "Automatic TOC regeneration", "Regenerate the future vault table of contents.", "repo-default", c => c.Defaults.AutomaticTocRegeneration, true),
            IntegerSetting("memory.maxFts5Results", "Documents & Memory", "Maximum FTS5 results", "Maximum results returned by future memory searches.", "repo-default", c => (long)c.Defaults.MaxFts5Results, 1, 1000, true, unit: "results"),
            BooleanSetting("memory.autoMaterializeGeneratedMemory", "Documents & Memory", "Materialize generated memory", "Populate the generated memory section in a later phase.", "repo-default", c => c.Defaults.AutoMaterializeGeneratedMemory, true),
            StringSetting("automation.timezone", "Automation", "Timezone", "Timezone used by future scheduled jobs.", "repo-default", ResolveTimezone, true, true, true, false, new SettingConstraints(Format: "iana-timezone"), ValidateTimezone),
            EnumSetting("automation.missedRunPolicy", "Automation", "Missed-run policy", "How a future scheduler handles missed work.", "repo-default", ["skip", "run-once"], c => c.Defaults.MissedRunPolicy, true),
            IntegerSetting("automation.maxQueuedPromptsPerAgent", "Automation", "Maximum queued prompts", "Maximum future scheduled prompts queued per agent.", "repo-default", c => (long)c.Defaults.MaxQueuedPromptsPerAgent, 0, 100, true, unit: "prompts"),
            StringSetting("system.runtimeDirectory", "System", "Runtime directory", "Startup directory for SQLite and runtime artifacts.", "environment", c => c.Bootstrap.RuntimeDirectory, false, false, true, true, new SettingConstraints(Format: "path")),
            StringSetting("system.serverHost", "System", "Server host", "Startup bind host for the local server.", "environment", c => c.Bootstrap.ServerHost, false, false, true, true),
            IntegerSetting("system.serverPort", "System", "Server port", "Startup bind port for the local server.", "environment", c => (long)c.Bootstrap.ServerPort, 1, 65535, true, false, false, true),
            StringSetting("system.tmuxPrefix", "System", "tmux prefix", "Stable prefix for future managed sessions.", "environment", c => c.Bootstrap.TmuxPrefix, false, false, true, true),
            LockedStatus("safety.emailSending", "Email sending", "capability-policy", "No email-send capability exists.", c => c.Policies.EmailSendingDisabled ? "Disabled" : "Available"),
            LockedStatus("safety.unverifiedMessageRecipients", "Unverified message recipients", "capability-policy", "Outbound messaging requires a verified contact.", c => c.Policies.UnverifiedMessageRecipientsBlocked ? "Blocked" : "Allowed"),
            LockedStatus("safety.groupMessaging", "Group messaging", "capability-policy", "Group destinations are disabled.", c => c.Policies.GroupMessagingDisabled ? "Disabled" : "Enabled"),
            LockedStatus("safety.crossRealmFallback", "Cross-realm fallback", "realm-policy", "Work and personal resources never fall back to one another.", c => c.Policies.CrossRealmFallbackDenied ? "Denied" : "Allowed"),
            LockedStatus("safety.consequentialAudit", "Consequential action audit", "capability-policy", "Consequential actions create immutable activity events.", c => c.Policies.ConsequentialAuditRequired ? "Required" : "Optional"),
            LockedStatus("safety.checkpointBeforeRotation", "Checkpoint before rotation", "runtime-policy", "Session clear and rotation checkpoint durable state first.", c => c.Policies.CheckpointBeforeRotationRequired ? "Required" : "Optional")
        ]);
    }

    private static object ResolveTimezone(SettingsContext context) =>
        string.Equals(context.Defaults.AutomationTimezone, "local", StringComparison.OrdinalIgnoreCase)
            ? TimeZoneInfo.Local.Id
            : context.Defaults.AutomationTimezone;

    private static object NormalizePath(object? value, SettingsContext context)
    {
        var raw = value as string ?? string.Empty;
        if (raw.StartsWith("~/", StringComparison.Ordinal))
        {
            raw = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), raw[2..]);
        }

        return Path.GetFullPath(raw);
    }

    private static string? ValidatePath(object? value, SettingsContext context)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || path.Contains('\0') || path.Contains('\n') || path.Contains('\r'))
        {
            return "must be a non-empty path without control characters";
        }

        return null;
    }

    private static string? ValidateTimezone(object? value, SettingsContext context)
    {
        if (value is not string timezone)
        {
            return "must be a timezone string";
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return null;
        }
        catch (TimeZoneNotFoundException)
        {
            return "must be a recognized timezone";
        }
        catch (InvalidTimeZoneException)
        {
            return "must be a valid timezone";
        }
    }

    private static long GetInt64(IReadOnlyDictionary<string, object?> values, string key) =>
        values[key] is long value ? value : Convert.ToInt64(values[key], CultureInfo.InvariantCulture);

    private static string GetString(IReadOnlyDictionary<string, object?> values, string key) =>
        values[key] as string ?? throw new SettingsStoreException($"Effective value for {key} is not a string.");
}
