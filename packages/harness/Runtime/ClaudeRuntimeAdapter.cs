using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness.Runtime;

public interface IClaudeRuntimeAdapter
{
    ClaudeStartResult Start(AgentDefinition agent, PersistedSession session);

    TmuxHealth GetStatus(AgentDefinition agent, PersistedSession session);

    ClaudeResumeResult TryResume(AgentDefinition agent, PersistedSession session);

    void StartNewConversation(AgentDefinition agent, PersistedSession session);

    string RecordConversationReference(AgentDefinition agent, PersistedSession session, string reference);

    void Stop(AgentDefinition agent, PersistedSession session);
}

public sealed record ClaudeStartResult(bool ResumeAttempted, bool StartedNewConversation);

public sealed record ClaudeResumeResult(bool Attempted, bool Available);

public sealed class ClaudeRuntimeAdapter : IClaudeRuntimeAdapter
{
    private readonly TmuxSessionManager tmux;
    private readonly string executable;

    public ClaudeRuntimeAdapter(TmuxSessionManager tmux, string executable = "claude")
    {
        this.tmux = tmux;
        this.executable = executable;
    }

    public ClaudeStartResult Start(AgentDefinition agent, PersistedSession session)
    {
        var resume = TryResume(agent, session);
        if (resume.Attempted && resume.Available)
        {
            return new ClaudeStartResult(true, false);
        }

        StartNewConversation(agent, session);
        return new ClaudeStartResult(resume.Attempted, true);
    }

    public TmuxHealth GetStatus(AgentDefinition agent, PersistedSession session) =>
        tmux.GetHealth(session.TmuxSessionName, executable);

    public ClaudeResumeResult TryResume(AgentDefinition agent, PersistedSession session)
    {
        if (string.IsNullOrWhiteSpace(session.NativeConversationReference))
        {
            return new ClaudeResumeResult(false, false);
        }

        try
        {
            tmux.LaunchProcess(
                session.TmuxSessionName,
                agent.WorkingDirectory,
                executable,
                ["--resume", session.NativeConversationReference]);
            return new ClaudeResumeResult(true, true);
        }
        catch (TmuxOperationException)
        {
            return new ClaudeResumeResult(true, false);
        }
    }

    public void StartNewConversation(AgentDefinition agent, PersistedSession session) =>
        tmux.LaunchProcess(session.TmuxSessionName, agent.WorkingDirectory, executable, []);

    public string RecordConversationReference(AgentDefinition agent, PersistedSession session, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 512 || reference.Any(char.IsControl))
        {
            throw new AgentConfigurationException("The native conversation reference is invalid.");
        }

        return reference.Trim();
    }

    public void Stop(AgentDefinition agent, PersistedSession session) => tmux.StopSession(session.TmuxSessionName);
}
