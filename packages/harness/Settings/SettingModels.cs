using System.Text.Json;
using PersonalAssistant.Harness.Bootstrap;
using PersonalAssistant.Harness.Policies;

namespace PersonalAssistant.Harness.Settings;

public enum SettingValueType
{
    String,
    Integer,
    Boolean,
    Enum,
    Status
}

public enum SettingScopeType
{
    Global,
    Realm,
    Agent,
    Integration
}

public sealed record SettingConstraints(
    long? Minimum = null,
    long? Maximum = null,
    IReadOnlyList<string>? Options = null,
    string? Format = null,
    string? Unit = null);

public sealed record SettingsContext(
    string RepositoryRoot,
    BootstrapConfiguration Bootstrap,
    RuntimeDefaults Defaults,
    PolicySnapshot Policies);

public sealed record SettingScope(string Type, string? Id);

public sealed record SettingView(
    string Key,
    string Category,
    string Label,
    string Description,
    string ValueType,
    IReadOnlyList<string> Options,
    object? Value,
    object? DefaultValue,
    bool HasOverride,
    string Source,
    SettingScope Scope,
    bool Editable,
    bool Resettable,
    bool RequiresRestart,
    bool Bootstrap,
    bool Sensitive,
    SettingConstraints Constraints);

public sealed record SafetyView(
    string Key,
    string Label,
    string State,
    string Source,
    bool Locked,
    string Reason);

public sealed record IntegrationView(
    string Id,
    string Label,
    string Status,
    string Phase);

public sealed record SettingsSnapshot(
    string ContractVersion,
    IReadOnlyList<SettingView> Settings,
    IReadOnlyList<SafetyView> Safety,
    IReadOnlyList<IntegrationView> Integrations);

public sealed record SettingChange(
    string Key,
    JsonElement Value,
    SettingScopeType Scope = SettingScopeType.Global,
    string? ScopeId = null);

public sealed record SettingParseResult(bool Success, object? Value, string? Error)
{
    public static SettingParseResult Valid(object? value) => new(true, value, null);
    public static SettingParseResult Invalid(string error) => new(false, null, error);
}

public sealed class SettingDefinition
{
    public SettingDefinition(
        string key,
        string category,
        string label,
        string description,
        SettingValueType valueType,
        IReadOnlyList<string> options,
        SettingScopeType scope,
        bool editable,
        bool resettable,
        bool requiresRestart,
        bool bootstrap,
        bool sensitive,
        SettingConstraints constraints,
        string defaultSource,
        Func<SettingsContext, object?> defaultResolver,
        Func<JsonElement, SettingParseResult> parser,
        Func<object?, SettingsContext, string?>? validator = null,
        Func<object?, SettingsContext, object?>? normalizer = null)
    {
        Key = key;
        Category = category;
        Label = label;
        Description = description;
        ValueType = valueType;
        Options = options;
        Scope = scope;
        Editable = editable;
        Resettable = resettable;
        RequiresRestart = requiresRestart;
        Bootstrap = bootstrap;
        Sensitive = sensitive;
        Constraints = constraints;
        DefaultSource = defaultSource;
        DefaultResolver = defaultResolver;
        Parser = parser;
        Validator = validator;
        Normalizer = normalizer;
    }

    public string Key { get; }
    public string Category { get; }
    public string Label { get; }
    public string Description { get; }
    public SettingValueType ValueType { get; }
    public IReadOnlyList<string> Options { get; }
    public SettingScopeType Scope { get; }
    public bool Editable { get; }
    public bool Resettable { get; }
    public bool RequiresRestart { get; }
    public bool Bootstrap { get; }
    public bool Sensitive { get; }
    public SettingConstraints Constraints { get; }
    public string DefaultSource { get; }
    public Func<SettingsContext, object?> DefaultResolver { get; }
    public Func<JsonElement, SettingParseResult> Parser { get; }
    public Func<object?, SettingsContext, string?>? Validator { get; }
    public Func<object?, SettingsContext, object?>? Normalizer { get; }

    public string ValueTypeName => ValueType switch
    {
        SettingValueType.String => "string",
        SettingValueType.Integer => "integer",
        SettingValueType.Boolean => "boolean",
        SettingValueType.Enum => "enum",
        SettingValueType.Status => "status",
        _ => "string"
    };

    public object? Normalize(object? value, SettingsContext context) => Normalizer?.Invoke(value, context) ?? value;

    public string? Validate(object? value, SettingsContext context)
    {
        if (value is null)
        {
            return "value is required";
        }

        if (Constraints.Minimum is not null && value is long integer && integer < Constraints.Minimum)
        {
            return $"must be at least {Constraints.Minimum}";
        }

        if (Constraints.Maximum is not null && value is long maximumValue && maximumValue > Constraints.Maximum)
        {
            return $"must be at most {Constraints.Maximum}";
        }

        return Validator?.Invoke(value, context);
    }

    public static SettingParseResult ParseString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { } value
            ? SettingParseResult.Valid(value)
            : SettingParseResult.Invalid("must be a JSON string");

    public static SettingParseResult ParseInteger(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value)
            ? SettingParseResult.Valid(value)
            : SettingParseResult.Invalid("must be an integer JSON number");

    public static SettingParseResult ParseBoolean(JsonElement element) =>
        element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? SettingParseResult.Valid(element.GetBoolean())
            : SettingParseResult.Invalid("must be a JSON boolean");

    public static Func<JsonElement, SettingParseResult> ParseEnum(IReadOnlyList<string> options) => element =>
    {
        var parsed = ParseString(element);
        if (!parsed.Success)
        {
            return parsed;
        }

        return options.Contains((string)parsed.Value!, StringComparer.Ordinal)
            ? parsed
            : SettingParseResult.Invalid($"must be one of: {string.Join(", ", options)}");
    };
}
