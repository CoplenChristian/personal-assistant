using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PersonalAssistant.Server.Tests;

public sealed class AgentApiTests
{
    [Fact]
    public async Task Get_personal_returns_explicit_desired_and_observed_state()
    {
        using var factory = new SettingsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/agents/personal");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("phase-0b-agents.v1", body.GetProperty("contractVersion").GetString());
        Assert.Equal("personal", body.GetProperty("id").GetString());
        Assert.Equal("claude", body.GetProperty("runtime").GetString());
        Assert.Equal("stopped", body.GetProperty("desiredState").GetString());
        Assert.Equal("test-pa-personal", body.GetProperty("tmuxSessionName").GetString());
        Assert.False(body.GetProperty("runtimeHealthy").GetBoolean());
    }

    [Fact]
    public async Task Stop_personal_is_idempotent_when_no_session_exists()
    {
        using var factory = new SettingsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/agents/personal/stop", content: null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("stopped", body.GetProperty("desiredState").GetString());
        Assert.Equal("exited", body.GetProperty("observedState").GetString());
        Assert.False(body.GetProperty("sessionDetected").GetBoolean());
    }
}
