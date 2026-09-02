using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Persistence;
using Xunit;

namespace PersonalAssistant.Server.Tests;

public sealed class ActivityApiTests
{
    [Fact]
    public async Task Get_activity_returns_versioned_json_with_zero_counters_and_no_fake_integration_events()
    {
        ActivityTelemetry.ResetForTests();
        using var factory = new ActivityApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/activity?date=2026-09-01&timezone=UTC");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ActivityQueryService.ContractVersion, body.GetProperty("contractVersion").GetString());
        Assert.Equal("2026-09-01", body.GetProperty("date").GetString());
        Assert.Equal("UTC", body.GetProperty("timezone").GetString());
        Assert.Equal(ActivityCategoryKeys.All.Count, body.GetProperty("counters").EnumerateObject().Count());
        Assert.All(ActivityCategoryKeys.All, key =>
            Assert.Equal(0, body.GetProperty("counters").GetProperty(key).GetInt32()));
        Assert.Empty(body.GetProperty("recentEvents").EnumerateArray());
        Assert.False(body.GetProperty("auditDegraded").GetBoolean());
    }

    [Fact]
    public async Task Get_activity_returns_redacted_recent_events_and_blocked_failure_statuses()
    {
        ActivityTelemetry.ResetForTests();
        using var factory = new ActivityApiFactory();
        factory.Seed(new ActivityEvent(
            "blocked-hygiene",
            new DateTimeOffset(2026, 9, 1, 14, 0, 0, TimeSpan.Zero),
            "personal",
            "personal",
            "agents",
            "clear",
            "runtime-session",
            "blocked",
            null,
            """{"eventType":"agent.clear","outcome":"checkpoint_failed","errorCode":"checkpoint_write_failed"}"""));
        factory.Seed(new ActivityEvent(
            "blocked-event",
            new DateTimeOffset(2026, 9, 1, 16, 0, 0, TimeSpan.Zero),
            "personal",
            "personal",
            "agents",
            "clear",
            "runtime-session",
            "blocked",
            null,
            """{"eventType":"agent.clear","outcome":"checkpoint_failed"}"""));
        factory.Seed(new ActivityEvent(
            "failure-event",
            new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero),
            "personal",
            "personal",
            "agents",
            "rotate",
            "runtime-session",
            "failure",
            null,
            """{"eventType":"agent.rotate","outcome":"native_action_failed","input":"should-not-leak"}"""));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/activity?date=2026-09-01&timezone=UTC&limit=5");
        var payload = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(payload).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body.GetProperty("counters").GetProperty(ActivityCategoryKeys.AgentClears).GetInt32());
        Assert.Equal(1, body.GetProperty("counters").GetProperty(ActivityCategoryKeys.AgentRotations).GetInt32());
        Assert.Equal(0, body.GetProperty("counters").GetProperty(ActivityCategoryKeys.SecurityBlocked).GetInt32());
        Assert.Equal(1, body.GetProperty("counters").GetProperty(ActivityCategoryKeys.Failures).GetInt32());
        Assert.DoesNotContain("should-not-leak", payload, StringComparison.Ordinal);

        var events = body.GetProperty("recentEvents").EnumerateArray().ToArray();
        Assert.Equal(3, events.Length);
        Assert.Equal("blocked-event", events[0].GetProperty("id").GetString());
        Assert.Equal("blocked", events[0].GetProperty("status").GetString());
        Assert.Equal("failure-event", events[1].GetProperty("id").GetString());
        Assert.Equal("failure", events[1].GetProperty("status").GetString());
        Assert.Contains("[redacted]", events[1].GetProperty("metadataJson").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_activity_reports_audit_degraded_when_recording_has_failed()
    {
        ActivityTelemetry.ResetForTests();
        using var factory = new ActivityApiFactory();
        using var client = factory.CreateClient();

        ActivityTelemetry.TryRecord(new ThrowingActivitySink(), new ActivityEvent(
            "lost-event",
            DateTimeOffset.UtcNow,
            "personal",
            "personal",
            "agents",
            "start",
            "runtime-session",
            "success",
            null,
            """{"eventType":"test.event","outcome":"observed"}"""));

        using var response = await client.GetAsync("/api/activity?date=2026-09-01&timezone=UTC");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("auditDegraded").GetBoolean());
    }

    [Fact]
    public async Task Get_activity_rejects_invalid_query_parameters_with_problem_details()
    {
        ActivityTelemetry.ResetForTests();
        using var factory = new ActivityApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/activity?date=2026/09/01");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("activity_date_invalid", body.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed class ThrowingActivitySink : IActivityEventSink
    {
        public void Append(ActivityEvent activityEvent) =>
            throw new InvalidOperationException("telemetry unavailable");
    }
}

public sealed class ActivityApiFactory : WebApplicationFactory<Program>
{
    private readonly string runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-activity-api-").FullName;
    private SqliteHarnessDatabase? database;

    public void Seed(ActivityEvent activityEvent)
    {
        database ??= new SqliteHarnessDatabase(Path.Combine(runtimeDirectory, "personal-assistant.sqlite"));
        new SqliteActivityEventSink(database).Append(activityEvent);
    }

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
        if (disposing)
        {
            database?.Dispose();
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
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

        throw new InvalidOperationException("Unable to find repository root for activity API tests.");
    }
}
