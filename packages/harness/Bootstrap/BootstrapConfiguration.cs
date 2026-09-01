namespace PersonalAssistant.Harness.Bootstrap;

public sealed record BootstrapConfiguration(
    string RuntimeDirectory,
    string ServerHost,
    int ServerPort,
    string TmuxPrefix,
    string VaultDefaultPath,
    string VaultDefaultSource);

public sealed class BootstrapConfigurationException(string message) : Exception(message);
