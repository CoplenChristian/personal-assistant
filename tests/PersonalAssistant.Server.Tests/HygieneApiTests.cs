using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Memory;
using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Server.Tests;

public sealed class HygieneApiTests
{
    [Fact]
    public async Task Clear_route_returns_versioned_safe_result_and_typed_checkpoint()
    {
        using var factory = new HygieneApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agents/personal/hygiene/clear",
            new
            {
                requestId = "request-clear",
                checkpoint = new
                {
                    reason = "clear",
                    generatedMemory = "private memory text",
                    generatedHandoff = "private handoff text"
                }
            });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("phase-0c-session-hygiene.v1", body.GetProperty("contractVersion").GetString());
        Assert.Equal("clear", body.GetProperty("action").GetString());
        Assert.Equal("checkpoint-clear", body.GetProperty("checkpointId").GetString());
        Assert.Equal("running", body.GetProperty("desiredState").GetString());
        Assert.True(body.GetProperty("nativeActionPerformed").GetBoolean());
        Assert.DoesNotContain("private memory", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var request = Assert.Single(factory.Service.ActionRequests);
        Assert.Equal(SessionHygieneAction.Clear, request.Action);
        Assert.Equal("clear", request.Checkpoint.Reason);
        Assert.Equal("private memory text", request.Checkpoint.GeneratedMemory);
    }

    [Fact]
    public async Task Missing_checkpoint_is_rejected_before_service_invocation()
    {
        using var factory = new HygieneApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agents/personal/hygiene/compact",
            new { requestId = "missing-checkpoint" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("hygiene_request_invalid", body.GetProperty("code").GetString());
        Assert.Empty(factory.Service.ActionRequests);
    }

    [Fact]
    public async Task Checkpoint_failure_is_exposed_as_a_blocked_problem_without_payload()
    {
        using var factory = new HygieneApiFactory
        {
            Service = { ActionFailure = new CheckpointException("checkpoint_write_failed", "checkpoint unavailable") }
        };
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agents/personal/hygiene/rotate",
            new
            {
                requestId = "blocked-rotate",
                checkpoint = new
                {
                    reason = "rotate",
                    generatedMemory = "secret memory",
                    generatedHandoff = "secret handoff"
                }
            });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("checkpoint_write_failed", body.GetProperty("code").GetString());
        Assert.DoesNotContain("secret memory", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(factory.Service.ActionRequests);
    }

    [Fact]
    public async Task Concurrent_or_runtime_failures_use_stable_problem_details()
    {
        using var factory = new HygieneApiFactory
        {
            Service = { ActionFailure = new SessionHygieneException("hygiene_in_progress", "another action is active") }
        };
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agents/personal/hygiene/compact",
            ValidRequest("concurrent"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("hygiene_in_progress", body.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Checkpoint_route_returns_only_an_opaque_receipt()
    {
        using var factory = new HygieneApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/agents/personal/hygiene/checkpoint",
            new
            {
                requestId = "checkpoint-only",
                checkpoint = new
                {
                    reason = "compact",
                    generatedMemory = "private memory",
                    generatedHandoff = "private handoff"
                }
            });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("phase-0c-session-hygiene.v1", body.GetProperty("contractVersion").GetString());
        Assert.Equal("checkpoint-only", body.GetProperty("requestId").GetString());
        Assert.Equal("checkpoint-compact", body.GetProperty("checkpointId").GetString());
        Assert.DoesNotContain("private memory", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("compact", Assert.Single(factory.Service.CheckpointRequests).Request.Reason);
    }

    private static object ValidRequest(string requestId) => new
    {
        requestId,
        checkpoint = new
        {
            reason = "compact",
            generatedMemory = "memory",
            generatedHandoff = "handoff"
        }
    };
}

public sealed class HygieneApiFactory : WebApplicationFactory<Program>
{
    private readonly string runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-hygiene-api-").FullName;

    public RecordingHygieneService Service { get; set; } = new();

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
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISessionHygieneService>();
            services.AddSingleton<ISessionHygieneService>(_ => Service);
        });
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

        throw new InvalidOperationException("Unable to find repository root for hygiene API tests.");
    }
}

public sealed class RecordingHygieneService : ISessionHygieneService
{
    public List<SessionHygieneRequest> ActionRequests { get; } = [];
    public List<(string RequestId, CheckpointRequest Request)> CheckpointRequests { get; } = [];
    public Exception? ActionFailure { get; set; }

    public Task<SessionHygieneResult> ExecutePersonalAsync(
        SessionHygieneRequest request,
        CancellationToken cancellationToken = default)
    {
        ActionRequests.Add(request);
        if (ActionFailure is not null)
        {
            return Task.FromException<SessionHygieneResult>(ActionFailure);
        }

        return Task.FromResult(new SessionHygieneResult(
            request.RequestId,
            request.Action,
            $"checkpoint-{request.Checkpoint.Reason}",
            AgentDesiredState.Running,
            SessionObservedState.Running,
            NativeActionPerformed: true));
    }

    public Task<CheckpointReceipt> CheckpointPersonalAsync(
        string requestId,
        CheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckpointRequests.Add((requestId, request));
        return Task.FromResult(new CheckpointReceipt(requestId, $"checkpoint-{request.Reason}"));
    }
}
