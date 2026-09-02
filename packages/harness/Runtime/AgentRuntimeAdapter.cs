using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness.Runtime;

public interface IAgentRuntimeAdapter
{
    RuntimeStartResult Start(AgentDefinition agent, PersistedSession session);

    TmuxHealth GetStatus(AgentDefinition agent, PersistedSession session);

    RuntimeResumeResult TryResume(AgentDefinition agent, PersistedSession session);

    void StartNewConversation(AgentDefinition agent, PersistedSession session);

    string RecordConversationReference(AgentDefinition agent, PersistedSession session, string reference);

    void Stop(AgentDefinition agent, PersistedSession session);
}

public sealed record RuntimeStartResult(bool ResumeAttempted, bool StartedNewConversation);

public sealed record RuntimeResumeResult(bool Attempted, bool Available);

public interface IRuntimeAdapterResolver
{
    IAgentRuntimeAdapter Resolve(string runtime);
}

public sealed class RuntimeAdapterResolver : IRuntimeAdapterResolver
{
    private readonly IReadOnlyDictionary<string, IAgentRuntimeAdapter> adapters;

    public RuntimeAdapterResolver(IEnumerable<KeyValuePair<string, IAgentRuntimeAdapter>> adapters)
    {
        this.adapters = adapters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    public IAgentRuntimeAdapter Resolve(string runtime)
    {
        if (!adapters.TryGetValue(runtime, out var adapter))
        {
            throw new AgentLifecycleException("agent_runtime_unavailable", "The requested native runtime is unavailable.");
        }

        return adapter;
    }
}
