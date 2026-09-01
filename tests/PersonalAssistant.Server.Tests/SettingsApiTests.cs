using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PersonalAssistant.Server.Tests;

public sealed class SettingsApiTests
{
    [Fact]
    public async Task Get_returns_versioned_metadata_and_honest_integrations()
    {
        using var factory = new SettingsApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/settings");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("phase-0a-settings.v1", body.GetProperty("contractVersion").GetString());
        Assert.Contains(body.GetProperty("settings").EnumerateArray(), item => item.GetProperty("key").GetString() == "system.serverPort");
        Assert.Contains(body.GetProperty("safety").EnumerateArray(), item => item.GetProperty("key").GetString() == "safety.emailSending");
        Assert.All(body.GetProperty("integrations").EnumerateArray(), item => Assert.Equal("not-configured", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Patch_and_delete_round_trip_an_override()
    {
        using var factory = new SettingsApiFactory();
        using var client = factory.CreateClient();
        using var patchResponse = await client.PatchAsJsonAsync("/api/settings", new
        {
            changes = new[] { new { key = "appearance.theme", value = "dark" } }
        });
        var patched = await patchResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var resetResponse = await client.DeleteAsync("/api/settings/appearance.theme");
        var reset = await resetResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        Assert.Equal("dark", FindSetting(patched, "appearance.theme").GetProperty("value").GetString());
        Assert.True(FindSetting(patched, "appearance.theme").GetProperty("hasOverride").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        Assert.Equal("system", FindSetting(reset, "appearance.theme").GetProperty("value").GetString());
        Assert.False(FindSetting(reset, "appearance.theme").GetProperty("hasOverride").GetBoolean());
    }

    [Fact]
    public async Task Patch_rejects_bootstrap_setting_with_problem_details()
    {
        using var factory = new SettingsApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsJsonAsync("/api/settings", new
        {
            changes = new[] { new { key = "system.serverPort", value = 9999 } }
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bootstrap_setting", body.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Patch_rejects_invalid_batch_without_partial_change()
    {
        using var factory = new SettingsApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsJsonAsync("/api/settings", new
        {
            changes = new object[]
            {
                new { key = "appearance.theme", value = "dark" },
                new { key = "appearance.browserScrollbackLines", value = 0 }
            }
        });

        using var getResponse = await client.GetAsync("/api/settings");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("system", FindSetting(body, "appearance.theme").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Patch_rejects_null_change_with_problem_details()
    {
        using var factory = new SettingsApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsJsonAsync("/api/settings", new
        {
            changes = new object?[] { null }
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", body.GetProperty("code").GetString());
    }

    private static JsonElement FindSetting(JsonElement snapshot, string key) =>
        snapshot.GetProperty("settings").EnumerateArray().Single(item => item.GetProperty("key").GetString() == key);
}

public sealed class SettingsApiFactory : WebApplicationFactory<Program>
{
    private readonly string runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-api-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var repositoryRoot = FindRepositoryRoot();
        builder.UseEnvironment("Development");
        builder.UseSetting("PA_REPOSITORY_ROOT", repositoryRoot);
        builder.UseSetting("PA_RUNTIME_DIR", runtimeDirectory);
        builder.UseSetting("PA_SERVER_HOST", "127.0.0.1");
        builder.UseSetting("PA_SERVER_PORT", "4317");
        builder.UseSetting("PA_TMUX_PREFIX", "test-pa-");
        builder.UseSetting("PA_VAULT_DIR", Path.Combine(runtimeDirectory, "vault"));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PA_REPOSITORY_ROOT"] = repositoryRoot,
            ["PA_RUNTIME_DIR"] = runtimeDirectory,
            ["PA_SERVER_HOST"] = "127.0.0.1",
            ["PA_SERVER_PORT"] = "4317",
            ["PA_TMUX_PREFIX"] = "test-pa-",
            ["PA_VAULT_DIR"] = Path.Combine(runtimeDirectory, "vault")
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(runtimeDirectory))
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "policies", "defaults", "runtime.yaml")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to find repository root for API tests.");
    }
}
