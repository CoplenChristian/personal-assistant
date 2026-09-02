using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PersonalAssistant.Harness.Agents;

public sealed class AgentRegistry
{
    private static readonly Regex AgentIdPattern = new("^[a-z][a-z0-9-]{0,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string repositoryRoot;
    private readonly string tmuxPrefix;

    public AgentRegistry(string repositoryRoot, string tmuxPrefix)
    {
        this.repositoryRoot = Path.GetFullPath(repositoryRoot);
        this.tmuxPrefix = tmuxPrefix;
    }

    public AgentDefinition LoadPersonal() => LoadReviewed("personal", "claude", "personal");

    public AgentDefinition LoadWork() => LoadReviewed("work", "codex", "work");

    public AgentDefinition LoadReviewed(string expectedId, string expectedRuntime, string expectedRealm)
    {
        var path = Path.Combine(repositoryRoot, "agents", expectedId, "agent.yaml");
        if (!File.Exists(path))
        {
            throw new AgentConfigurationException($"The reviewed {expectedId} agent definition is missing.");
        }

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var manifest = deserializer.Deserialize<AgentManifest>(File.ReadAllText(path))
                ?? throw new AgentConfigurationException($"The {expectedId} agent definition is empty.");

            ValidateIdentity(manifest.Id);
            if (!string.Equals(manifest.Id, expectedId, StringComparison.Ordinal))
            {
                throw new AgentConfigurationException($"The reviewed {expectedId} agent definition must use id {expectedId}.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new AgentConfigurationException($"The {expectedId} agent definition requires a name.");
            }

            if (!string.Equals(manifest.Runtime, expectedRuntime, StringComparison.Ordinal))
            {
                throw new AgentConfigurationException($"The reviewed {expectedId} agent runtime must be {expectedRuntime}.");
            }

            var workingDirectory = ResolveWorkingDirectory(manifest.WorkingDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                throw new AgentConfigurationException($"The {expectedId} agent working directory does not exist.");
            }

            var realms = ValidateList(manifest.Realms, "realm");
            if (!realms.Contains(expectedRealm, StringComparer.Ordinal))
            {
                throw new AgentConfigurationException($"The reviewed {expectedId} agent definition must include the {expectedRealm} realm.");
            }

            var skills = ValidateList(manifest.Skills, "skill");
            var scheduledPermissions = ValidateList(manifest.ScheduledTaskPermissions, "scheduled permission", allowEmpty: true);
            var sessionName = tmuxPrefix + manifest.Id;
            ValidateSessionName(sessionName);

            return new AgentDefinition(
                manifest.Id,
                manifest.Name.Trim(),
                manifest.Runtime,
                workingDirectory,
                realms,
                skills,
                manifest.AutoStart,
                manifest.BrowserProfile,
                manifest.MemoryScope,
                scheduledPermissions,
                sessionName,
                path);
        }
        catch (AgentConfigurationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AgentConfigurationException($"Unable to load the {expectedId} agent definition: {exception.Message}");
        }
    }

    public static void ValidateIdentity(string id)
    {
        if (!AgentIdPattern.IsMatch(id ?? string.Empty))
        {
            throw new AgentConfigurationException("Agent id must start with a lowercase letter and contain only lowercase letters, numbers, and hyphens.");
        }
    }

    public static void ValidateSessionName(string sessionName)
    {
        if (sessionName.Length is 0 or > 128
            || sessionName.Any(char.IsWhiteSpace)
            || sessionName.Any(character => character is ':' or '/' or '\\'))
        {
            throw new AgentConfigurationException("The tmux session name is not safe.");
        }
    }

    private string ResolveWorkingDirectory(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return repositoryRoot;
        }

        var trimmed = rawPath.Trim();
        return Path.GetFullPath(trimmed, repositoryRoot);
    }

    private static IReadOnlyList<string> ValidateList(IReadOnlyList<string>? values, string label, bool allowEmpty = false)
    {
        var normalized = (values ?? []).Select(value => value?.Trim() ?? string.Empty).ToArray();
        if (!allowEmpty && normalized.Length == 0)
        {
            throw new AgentConfigurationException($"The agent definition requires at least one {label}.");
        }

        if (normalized.Any(value => value.Length == 0))
        {
            throw new AgentConfigurationException($"The agent definition contains an empty {label}.");
        }

        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new AgentConfigurationException($"The agent definition contains duplicate {label} values.");
        }

        return normalized;
    }

    private sealed class AgentManifest
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Runtime { get; set; } = string.Empty;
        public string? WorkingDirectory { get; set; }
        public List<string> Realms { get; set; } = [];
        public List<string> Skills { get; set; } = [];
        public bool AutoStart { get; set; }
        public string? BrowserProfile { get; set; }
        public string? MemoryScope { get; set; }
        public List<string> ScheduledTaskPermissions { get; set; } = [];
    }
}
