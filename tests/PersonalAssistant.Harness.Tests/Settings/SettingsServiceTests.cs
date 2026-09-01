using System.Text.Json;
using PersonalAssistant.Harness.Settings;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Settings;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Defaults_are_returned_without_override()
    {
        using var context = new SettingsTestContext();
        var snapshot = context.CreateService().GetSnapshot();

        var theme = snapshot.Settings.Single(setting => setting.Key == "appearance.theme");
        Assert.Equal("system", theme.Value);
        Assert.Equal("system", theme.DefaultValue);
        Assert.False(theme.HasOverride);
        Assert.Equal("repo-default", theme.Source);
    }

    [Fact]
    public void Valid_override_persists_and_reset_restores_default()
    {
        using var context = new SettingsTestContext();
        var service = context.CreateService();

        service.ApplyChanges([Change("appearance.theme", "dark")]);
        var overridden = service.GetSnapshot().Settings.Single(setting => setting.Key == "appearance.theme");
        Assert.Equal("dark", overridden.Value);
        Assert.True(overridden.HasOverride);
        Assert.Equal("override", overridden.Source);

        var secondService = context.CreateService();
        Assert.Equal("dark", secondService.GetSnapshot().Settings.Single(setting => setting.Key == "appearance.theme").Value);

        var reset = secondService.Reset("appearance.theme");
        var restored = reset.Settings.Single(setting => setting.Key == "appearance.theme");
        Assert.Equal("system", restored.Value);
        Assert.False(restored.HasOverride);
        Assert.Equal(2, context.Store.ReadActivityEvents().Count);
    }

    [Fact]
    public void Unknown_key_is_rejected()
    {
        using var context = new SettingsTestContext();
        var exception = Assert.Throws<SettingsException>(() =>
            context.CreateService().ApplyChanges([Change("unknown.setting", true)]));

        Assert.Equal("unknown_setting", exception.Code);
        Assert.Empty(context.Store.ReadGlobalOverrides());
    }

    [Fact]
    public void Invalid_enum_is_rejected_server_side()
    {
        using var context = new SettingsTestContext();
        var exception = Assert.Throws<SettingsException>(() =>
            context.CreateService().ApplyChanges([Change("appearance.theme", "neon")]));

        Assert.Equal("invalid_value", exception.Code);
        Assert.Empty(context.Store.ReadGlobalOverrides());
    }

    [Fact]
    public void Rotate_threshold_must_be_greater_than_warning_threshold()
    {
        using var context = new SettingsTestContext();
        var exception = Assert.Throws<SettingsException>(() =>
            context.CreateService().ApplyChanges([
                Change("sessions.nativeSessionRotateBytes", context.Defaults.NativeSessionWarningBytes)
            ]));

        Assert.Equal("cross_setting_invalid", exception.Code);
        Assert.Empty(context.Store.ReadGlobalOverrides());
    }

    [Fact]
    public void Patch_is_atomic_when_one_change_is_invalid()
    {
        using var context = new SettingsTestContext();
        var service = context.CreateService();

        Assert.Throws<SettingsException>(() => service.ApplyChanges([
            Change("appearance.theme", "dark"),
            Change("appearance.browserScrollbackLines", 0)
        ]));

        Assert.Equal("system", service.GetSnapshot().Settings.Single(setting => setting.Key == "appearance.theme").Value);
        Assert.Empty(context.Store.ReadActivityEvents());
    }

    [Fact]
    public void Safety_and_bootstrap_values_cannot_be_changed()
    {
        using var context = new SettingsTestContext();
        var service = context.CreateService();

        var safetyException = Assert.Throws<SettingsException>(() =>
            service.ApplyChanges([Change("safety.emailSending", "Available")]));
        var bootstrapException = Assert.Throws<SettingsException>(() =>
            service.ApplyChanges([Change("system.serverPort", 9999)]));

        Assert.Equal("immutable_setting", safetyException.Code);
        Assert.Equal("bootstrap_setting", bootstrapException.Code);
        Assert.Empty(context.Store.ReadGlobalOverrides());
    }

    [Fact]
    public void Sensitive_definitions_are_rejected_by_generic_store()
    {
        using var context = new SettingsTestContext();
        var sensitive = new SettingDefinition(
            "integration.secret",
            "Integrations",
            "Secret",
            "Should never be stored.",
            SettingValueType.String,
            Array.Empty<string>(),
            SettingScopeType.Global,
            true,
            true,
            false,
            false,
            true,
            new SettingConstraints(),
            "code-default",
            _ => string.Empty,
            SettingDefinition.ParseString);
        var registry = new SettingsRegistry(context.Registry.Definitions.Append(sensitive));

        var exception = Assert.Throws<SettingsException>(() =>
            context.CreateService(registry).ApplyChanges([Change("integration.secret", "value")]));

        Assert.Equal("sensitive_setting", exception.Code);
        Assert.Empty(context.Store.ReadGlobalOverrides());
    }

    [Fact]
    public void Restart_metadata_is_preserved_and_update_event_is_non_sensitive()
    {
        using var context = new SettingsTestContext();
        var service = context.CreateService();
        var snapshot = service.ApplyChanges([Change("sessions.tmuxHistoryLines", 20000)]);
        var setting = snapshot.Settings.Single(item => item.Key == "sessions.tmuxHistoryLines");
        var activity = context.Store.ReadActivityEvents().Single();

        Assert.True(setting.RequiresRestart);
        Assert.Contains("sessions.tmuxHistoryLines", activity.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("20000", activity.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain(context.Bootstrap.RuntimeDirectory, activity.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_future_scope_is_rejected()
    {
        using var context = new SettingsTestContext();
        var exception = Assert.Throws<SettingsException>(() => context.CreateService().ApplyChanges([
            new SettingChange("appearance.theme", JsonSerializer.SerializeToElement("dark"), SettingScopeType.Agent, "personal")
        ]));

        Assert.Equal("unsupported_scope", exception.Code);
    }

    [Fact]
    public void No_op_patch_preserves_unrelated_overrides()
    {
        using var context = new SettingsTestContext();
        var service = context.CreateService();

        service.ApplyChanges([Change("appearance.theme", "dark")]);
        service.ApplyChanges([Change("agents.defaults.autoStart", true)]);
        var snapshot = service.ApplyChanges([Change("appearance.theme", "dark")]);

        Assert.Equal("dark", snapshot.Settings.Single(item => item.Key == "appearance.theme").Value);
        Assert.True((bool)snapshot.Settings.Single(item => item.Key == "agents.defaults.autoStart").Value!);
        Assert.Equal(2, context.Store.ReadActivityEvents().Count);
    }

    [Fact]
    public void Patch_to_default_without_override_is_a_no_op()
    {
        using var context = new SettingsTestContext();
        var snapshot = context.CreateService().ApplyChanges([Change("appearance.theme", "system")]);

        var theme = snapshot.Settings.Single(item => item.Key == "appearance.theme");
        Assert.Equal("system", theme.Value);
        Assert.False(theme.HasOverride);
        Assert.Empty(context.Store.ReadGlobalOverrides());
        Assert.Empty(context.Store.ReadActivityEvents());
    }

    [Fact]
    public void Patch_to_default_without_override_preserves_unrelated_overrides()
    {
        using var context = new SettingsTestContext();
        var service = context.CreateService();

        service.ApplyChanges([Change("agents.defaults.autoStart", true)]);
        var snapshot = service.ApplyChanges([Change("appearance.theme", "system")]);

        Assert.Equal("system", snapshot.Settings.Single(item => item.Key == "appearance.theme").Value);
        Assert.True((bool)snapshot.Settings.Single(item => item.Key == "agents.defaults.autoStart").Value!);
        Assert.Single(context.Store.ReadGlobalOverrides());
        Assert.Equal("agents.defaults.autoStart", context.Store.ReadGlobalOverrides().Keys.Single());
        Assert.Single(context.Store.ReadActivityEvents());
    }

    [Fact]
    public void Setting_equal_to_baseline_removes_existing_override()
    {
        using var context = new SettingsTestContext();
        var service = context.CreateService();

        service.ApplyChanges([Change("appearance.theme", "dark")]);
        var snapshot = service.ApplyChanges([Change("appearance.theme", "system")]);

        var theme = snapshot.Settings.Single(item => item.Key == "appearance.theme");
        Assert.Equal("system", theme.Value);
        Assert.False(theme.HasOverride);
        Assert.DoesNotContain("appearance.theme", context.Store.ReadGlobalOverrides().Keys);
        Assert.Equal(2, context.Store.ReadActivityEvents().Count);
    }

    [Fact]
    public void Reset_without_override_is_idempotent_and_audit_free()
    {
        using var context = new SettingsTestContext();

        var snapshot = context.CreateService().Reset("appearance.theme");

        Assert.Equal("system", snapshot.Settings.Single(item => item.Key == "appearance.theme").Value);
        Assert.Empty(context.Store.ReadActivityEvents());
    }

    [Fact]
    public void Malformed_persisted_value_fails_closed()
    {
        using var context = new SettingsTestContext();
        context.SeedOverride("global", "", "appearance.theme", "not-json");

        var exception = Assert.Throws<SettingsStoreException>(() => context.CreateService().GetSnapshot());

        Assert.Equal("settings_store_invalid", exception.Code);
    }

    [Fact]
    public void Persisted_future_scope_fails_closed_instead_of_being_ignored()
    {
        using var context = new SettingsTestContext();
        context.SeedOverride("agent", "personal", "appearance.theme", "\"dark\"");

        var exception = Assert.Throws<SettingsStoreException>(() => context.CreateService().GetSnapshot());

        Assert.Equal("settings_store_invalid", exception.Code);
    }

    private static SettingChange Change(string key, object value) =>
        new(key, JsonSerializer.SerializeToElement(value));
}
