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
    public void Work_definition_loads_codex_runtime_and_work_realm()
    {
        var definition = new AgentRegistry(FindRepositoryRoot(), "test-pa-").LoadWork();

        Assert.Equal("work", definition.Id);
        Assert.Equal("codex", definition.Runtime);
        Assert.Contains("work", definition.Realms);
        Assert.Equal("test-pa-work", definition.TmuxSessionName);
    }

    [Fact]
    public void Work_definition_rejects_wrong_runtime()
    {
        using var fixture = new WorkManifestFixture("runtime: claude");

        Assert.Throws<AgentConfigurationException>(() => new AgentRegistry(fixture.Root, "test-pa-").LoadWork());
    }

    [Fact]
    public void Missing_working_directory_is_rejected()
    {
        using var fixture = new ManifestFixture("working_directory: /path/that/does/not/exist");

        Assert.Throws<AgentConfigurationException>(() => new AgentRegistry(fixture.Root, "test-pa-").LoadPersonal());
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

        throw new InvalidOperationException("Unable to find repository root for agent tests.");
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

    private sealed class WorkManifestFixture : IDisposable
    {
        public WorkManifestFixture(string replacement)
        {
            Root = Directory.CreateTempSubdirectory("personal-assistant-work-agent-").FullName;
            var workDirectory = Directory.CreateDirectory(Path.Combine(Root, "agents", "work"));
            var manifest = """
                id: work
                name: Work
                runtime: codex
                working_directory: null
                realms:
                  - work
                skills:
                  - agents
                auto_start: false
                browser_profile: work
                memory_scope: work
                scheduled_task_permissions: []
                """;
            manifest = manifest.Replace("runtime: codex", replacement, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(workDirectory.FullName, "agent.yaml"), manifest);
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
