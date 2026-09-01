using System.Globalization;
using System.Text.Json;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Persistence;

namespace PersonalAssistant.Harness.Settings;

public sealed class SettingsService
{
    public const string ContractVersion = "phase-0a-settings.v1";

    private readonly SettingsRegistry registry;
    private readonly SettingsContext context;
    private readonly ISettingsOverrideStore store;

    public SettingsService(SettingsRegistry registry, SettingsContext context, ISettingsOverrideStore store)
    {
        this.registry = registry;
        this.context = context;
        this.store = store;
    }

    public SettingsSnapshot GetSnapshot()
    {
        var overrides = ReadValidatedOverrides();
        var defaults = registry.ResolveDefaults(context);
        var values = new Dictionary<string, object?>(defaults, StringComparer.Ordinal);
        var hasOverrides = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, value) in overrides)
        {
            var definition = registry.Get(key);
            var parsed = ParsePersistedValue(definition, value);
            values[key] = parsed;
            hasOverrides.Add(key);
        }

        registry.ValidateCandidate(values, context);
        return BuildSnapshot(values, defaults, hasOverrides);
    }

    public SettingsSnapshot ApplyChanges(IReadOnlyCollection<SettingChange> changes)
    {
        if (changes.Count == 0)
        {
            throw new SettingsException("invalid_request", "At least one setting change is required.");
        }

        var duplicateKey = changes
            .GroupBy(change => change.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateKey is not null)
        {
            throw new SettingsException("invalid_request", $"Setting {duplicateKey} appears more than once.");
        }

        var existingOverrides = ReadValidatedOverrides();
        var defaults = registry.ResolveDefaults(context);
        var candidate = new Dictionary<string, object?>(defaults, StringComparer.Ordinal);
        foreach (var (existingKey, rawValue) in existingOverrides)
        {
            candidate[existingKey] = ParsePersistedValue(registry.Get(existingKey), rawValue);
        }

        var desiredOverrides = existingOverrides.ToDictionary(
            pair => pair.Key,
            pair => (string?)pair.Value,
            StringComparer.Ordinal);
        var touched = new List<SettingDefinition>();

        foreach (var change in changes)
        {
            ValidateGlobalScope(change);
            var definition = GetEditableDefinition(change.Key);
            var parsed = definition.Parser(change.Value);
            if (!parsed.Success)
            {
                throw new SettingsException("invalid_value", $"{definition.Key} {parsed.Error}.", new Dictionary<string, string>
                {
                    [definition.Key] = parsed.Error ?? "invalid value"
                });
            }

            var rawValidationError = definition.Validate(parsed.Value, context);
            if (rawValidationError is not null)
            {
                throw new SettingsException("invalid_value", $"{definition.Key} {rawValidationError}.", new Dictionary<string, string>
                {
                    [definition.Key] = rawValidationError
                });
            }

            object? normalized;
            try
            {
                normalized = definition.Normalize(parsed.Value, context);
            }
            catch (Exception)
            {
                throw new SettingsException("invalid_value", $"{definition.Key} is not a valid value.", new Dictionary<string, string>
                {
                    [definition.Key] = "must be valid for its declared type"
                });
            }

            var validationError = definition.Validate(normalized, context);
            if (validationError is not null)
            {
                throw new SettingsException("invalid_value", $"{definition.Key} {validationError}.", new Dictionary<string, string>
                {
                    [definition.Key] = validationError
                });
            }

            candidate[definition.Key] = normalized;
            var baseline = defaults[definition.Key];
            desiredOverrides[definition.Key] = ValuesEqual(normalized, baseline)
                ? null
                : SerializeValue(normalized);
            touched.Add(definition);
        }

        registry.ValidateCandidate(candidate, context);
        var changesToStore = desiredOverrides
            .Where(pair => touched.Any(definition => string.Equals(definition.Key, pair.Key, StringComparison.Ordinal)))
            .Where(pair => existingOverrides.TryGetValue(pair.Key, out var current)
                ? !string.Equals(current, pair.Value, StringComparison.Ordinal)
                : pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        if (changesToStore.Count == 0)
        {
            return BuildSnapshot(candidate, defaults, existingOverrides.Keys.ToHashSet(StringComparer.Ordinal));
        }

        var changedDefinitions = touched
            .Where(definition => changesToStore.ContainsKey(definition.Key))
            .ToArray();
        var activity = ActivityEvent.SettingsUpdated(
            changesToStore.Keys.ToArray(),
            changedDefinitions.Any(definition => definition.RequiresRestart),
            "patch");
        store.ApplyAtomic(changesToStore, activity);
        return GetSnapshot();
    }

    public SettingsSnapshot Reset(string key)
    {
        var definition = GetEditableDefinition(key);
        var existingOverrides = ReadValidatedOverrides();
        if (!existingOverrides.ContainsKey(key))
        {
            return GetSnapshot();
        }

        var defaults = registry.ResolveDefaults(context);
        var candidate = new Dictionary<string, object?>(defaults, StringComparer.Ordinal);
        foreach (var (existingKey, rawValue) in existingOverrides.Where(pair => !string.Equals(pair.Key, key, StringComparison.Ordinal)))
        {
            candidate[existingKey] = ParsePersistedValue(registry.Get(existingKey), rawValue);
        }

        registry.ValidateCandidate(candidate, context);
        var activity = ActivityEvent.SettingsUpdated([key], definition.RequiresRestart, "reset");
        store.ApplyAtomic(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = null }, activity);
        return GetSnapshot();
    }

    private IReadOnlyDictionary<string, string> ReadValidatedOverrides()
    {
        var overrides = store.ReadGlobalOverrides();
        foreach (var key in overrides.Keys)
        {
            if (!registry.TryGet(key, out var definition) || definition is null)
            {
                throw new SettingsStoreException($"Persisted override for unknown setting {key}.");
            }

            if (definition.Scope != SettingScopeType.Global || definition.Bootstrap || !definition.Editable || definition.Sensitive)
            {
                throw new SettingsStoreException($"Persisted override for {key} is not an editable global setting.");
            }

            _ = ParsePersistedValue(definition, overrides[key]);
        }

        return overrides;
    }

    private object? ParsePersistedValue(SettingDefinition definition, string rawValue)
    {
        try
        {
            using var document = JsonDocument.Parse(rawValue);
            var parsed = definition.Parser(document.RootElement);
            if (!parsed.Success)
            {
                throw new SettingsStoreException($"Persisted value for {definition.Key} is invalid: {parsed.Error}.");
            }

            var rawValidationError = definition.Validate(parsed.Value, context);
            if (rawValidationError is not null)
            {
                throw new SettingsStoreException($"Persisted value for {definition.Key} is invalid: {rawValidationError}.");
            }

            object? normalized;
            try
            {
                normalized = definition.Normalize(parsed.Value, context);
            }
            catch (Exception)
            {
                throw new SettingsStoreException($"Persisted value for {definition.Key} is not valid for its declared type.");
            }

            var validationError = definition.Validate(normalized, context);
            if (validationError is not null)
            {
                throw new SettingsStoreException($"Persisted value for {definition.Key} is invalid: {validationError}.");
            }

            return normalized;
        }
        catch (SettingsStoreException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SettingsStoreException($"Persisted value for {definition.Key} is not valid JSON: {exception.Message}");
        }
    }

    private SettingDefinition GetEditableDefinition(string key)
    {
        var definition = registry.Get(key);
        if (definition.Sensitive)
        {
            throw new SettingsException("sensitive_setting", $"Setting {key} cannot be stored in generic settings.");
        }

        if (definition.Bootstrap)
        {
            throw new SettingsException("bootstrap_setting", $"Setting {key} is controlled by startup configuration.");
        }

        if (!definition.Editable || !definition.Resettable)
        {
            throw new SettingsException("immutable_setting", $"Setting {key} is read-only.");
        }

        return definition;
    }

    private static void ValidateGlobalScope(SettingChange change)
    {
        if (change.Scope != SettingScopeType.Global || !string.IsNullOrEmpty(change.ScopeId))
        {
            throw new SettingsException("unsupported_scope", "Phase 0A accepts only global settings with an empty scope ID.");
        }
    }

    private SettingsSnapshot BuildSnapshot(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, object?> defaults,
        ISet<string> hasOverrides)
    {
        var settingViews = registry.Definitions
            .Where(definition => !definition.Key.StartsWith("safety.", StringComparison.Ordinal))
            .Select(definition => new SettingView(
                definition.Key,
                definition.Category,
                definition.Label,
                definition.Description,
                definition.ValueTypeName,
                definition.Options,
                values[definition.Key],
                defaults[definition.Key],
                hasOverrides.Contains(definition.Key),
                hasOverrides.Contains(definition.Key) ? "override" : ResolveSource(definition),
                new SettingScope("global", null),
                definition.Editable,
                definition.Resettable,
                definition.RequiresRestart,
                definition.Bootstrap,
                definition.Sensitive,
                definition.Constraints))
            .ToArray();

        var safety = registry.Definitions
            .Where(definition => definition.Key.StartsWith("safety.", StringComparison.Ordinal))
            .Select(definition => new SafetyView(
                definition.Key,
                definition.Label,
                values[definition.Key]?.ToString() ?? "Unknown",
                definition.DefaultSource,
                true,
                definition.Description))
            .ToArray();

        var integrations = new[]
        {
            new IntegrationView("email", "Email", "not-configured", "Phase 4"),
            new IntegrationView("calendar", "Calendar & Reminders", "not-configured", "Phase 3"),
            new IntegrationView("bluebubbles", "BlueBubbles", "not-configured", "Phase 5"),
            new IntegrationView("browser", "Browser", "not-configured", "Phase 7")
        };

        return new SettingsSnapshot(ContractVersion, settingViews, safety, integrations);
    }

    private string ResolveSource(SettingDefinition definition) =>
        definition.Key == "documents.vaultPath" ? context.Bootstrap.VaultDefaultSource : definition.DefaultSource;

    private static string SerializeValue(object? value) => JsonSerializer.Serialize(value);

    private static bool ValuesEqual(object? left, object? right) =>
        left switch
        {
            long leftLong when right is long rightLong => leftLong == rightLong,
            bool leftBool when right is bool rightBool => leftBool == rightBool,
            string leftString when right is string rightString => string.Equals(leftString, rightString, StringComparison.Ordinal),
            _ => Equals(left, right)
        };
}
