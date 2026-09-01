using PersonalAssistant.Harness.Agents;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Agents;

public sealed class AgentRegistryTests
{
    [Fact]
    public void Null_working_directory_uses_repository_root()
    {
        using var fixture = new ManifestFixture("working_directory: null");

        var definition = new AgentRegistry(fixture.Root, "test-pa-").LoadPersonal();

        Assert.Equal(Path.GetFullPath(fixture.Root), definition.WorkingDirectory);
        Assert.Equal("test-pa-personal", definition.TmuxSessionName);
    }

    [Fact]
    public void Invalid_agent_id_is_rejected()
    {
        using var fixture = new ManifestFixture("id: Personal");

        Assert.Throws<AgentConfigurationException>(() => new AgentRegistry(fixture.Root, "test-pa-").LoadPersonal());
    }

    [Fact]
    public void Missing_working_directory_is_rejected()
    {
        using var fixture = new ManifestFixture("working_directory: /path/that/does/not/exist");

        Assert.Throws<AgentConfigurationException>(() => new AgentRegistry(fixture.Root, "test-pa-").LoadPersonal());
    }

    private sealed class ManifestFixture : IDisposable
    {
        public ManifestFixture(string replacement)
        {
            Root = Directory.CreateTempSubdirectory("personal-assistant-agent-").FullName;
            var personalDirectory = Directory.CreateDirectory(Path.Combine(Root, "agents", "personal"));
            var manifest = """
                id: personal
                name: Personal
                runtime: claude
                working_directory: null
                realms:
                  - personal
                skills:
                  - agents
                auto_start: false
                browser_profile: personal
                memory_scope: personal
                scheduled_task_permissions: []
                """;
            if (replacement.StartsWith("id:", StringComparison.Ordinal))
            {
                manifest = manifest.Replace("id: personal", replacement, StringComparison.Ordinal);
            }
            else
            {
                manifest = manifest.Replace("working_directory: null", replacement, StringComparison.Ordinal);
            }

            File.WriteAllText(Path.Combine(personalDirectory.FullName, "agent.yaml"), manifest);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
