namespace PersonalAssistant.Harness.Settings;

public class SettingsException(string code, string message, IReadOnlyDictionary<string, string>? fields = null) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string> Fields { get; } = fields ?? new Dictionary<string, string>();
}

public sealed class SettingsStoreException(string message) : SettingsException("settings_store_invalid", message);
