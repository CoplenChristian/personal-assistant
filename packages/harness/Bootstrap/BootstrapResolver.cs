using System.Globalization;

namespace PersonalAssistant.Harness.Bootstrap;

public static class BootstrapResolver
{
    public static BootstrapConfiguration Resolve(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? baseDirectory = null,
        string? homeDirectory = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var basePath = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory());
        var home = Path.GetFullPath(homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var values = environment ?? ReadEnvironment();

        var runtimeDirectory = ResolvePath(Get(values, "PA_RUNTIME_DIR", "./runtime"), basePath, home);
        var serverHost = Get(values, "PA_SERVER_HOST", "127.0.0.1").Trim();
        ValidateServerHost(serverHost);

        var portText = Get(values, "PA_SERVER_PORT", "4317").Trim();
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var serverPort) || serverPort is < 1 or > 65535)
        {
            throw new BootstrapConfigurationException("PA_SERVER_PORT must be an integer between 1 and 65535.");
        }

        var tmuxPrefix = Get(values, "PA_TMUX_PREFIX", "pa-").Trim();
        if (tmuxPrefix.Length == 0 || tmuxPrefix.Any(char.IsWhiteSpace) || tmuxPrefix.Contains('/') || tmuxPrefix.Contains('\\'))
        {
            throw new BootstrapConfigurationException("PA_TMUX_PREFIX must be a non-empty session-safe prefix.");
        }

        var vaultFromEnvironment = !string.IsNullOrWhiteSpace(values.GetValueOrDefault("PA_VAULT_DIR"));
        var vaultPath = ResolvePath(Get(values, "PA_VAULT_DIR", "~/PersonalAssistantVault"), basePath, home);
        if (IsWithin(root, vaultPath))
        {
            throw new BootstrapConfigurationException("PA_VAULT_DIR cannot point inside the source repository.");
        }

        return new BootstrapConfiguration(
            runtimeDirectory,
            serverHost,
            serverPort,
            tmuxPrefix,
            vaultPath,
            vaultFromEnvironment ? "environment" : "system");
    }

    public static bool IsWithin(string parent, string candidate)
    {
        var parentPath = EnsureTrailingSeparator(Path.GetFullPath(parent));
        var candidatePath = Path.GetFullPath(candidate);
        return candidatePath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidatePath, parentPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string?> ReadEnvironment() =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PA_RUNTIME_DIR"] = Environment.GetEnvironmentVariable("PA_RUNTIME_DIR"),
            ["PA_SERVER_HOST"] = Environment.GetEnvironmentVariable("PA_SERVER_HOST"),
            ["PA_SERVER_PORT"] = Environment.GetEnvironmentVariable("PA_SERVER_PORT"),
            ["PA_TMUX_PREFIX"] = Environment.GetEnvironmentVariable("PA_TMUX_PREFIX"),
            ["PA_VAULT_DIR"] = Environment.GetEnvironmentVariable("PA_VAULT_DIR")
        };

    private static string Get(IReadOnlyDictionary<string, string?> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string ResolvePath(string raw, string baseDirectory, string homeDirectory)
    {
        var expanded = raw.Trim();
        if (expanded == "~")
        {
            expanded = homeDirectory;
        }
        else if (expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            expanded = Path.Combine(homeDirectory, expanded[2..]);
        }

        return Path.GetFullPath(expanded, baseDirectory);
    }

    private static void ValidateServerHost(string host)
    {
        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || host is "0.0.0.0" or "::" or "*" or "+")
        {
            throw new BootstrapConfigurationException("PA_SERVER_HOST must be a non-wildcard host; the default is 127.0.0.1.");
        }
    }

    private static string EnsureTrailingSeparator(string value) =>
        value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;
}
