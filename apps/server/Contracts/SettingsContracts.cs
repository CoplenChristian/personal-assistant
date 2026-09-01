using System.Text.Json;

namespace PersonalAssistant.Server.Contracts;

public sealed record SettingsPatchRequest(IReadOnlyList<SettingsChangeRequest?>? Changes);

public sealed record SettingsChangeRequest(
    string? Key,
    JsonElement Value,
    SettingsScopeRequest? Scope = null);

public sealed record SettingsScopeRequest(string? Type, string? Id);
