using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness.Runtime;

public sealed class CodexRuntimeAdapter : IAgentRuntimeAdapter
{
    private readonly TmuxSessionManager tmux;
    private readonly string executable;

    public CodexRuntimeAdapter(TmuxSessionManager tmux, string executable = "codex")
    {
        this.tmux = tmux;
        this.executable = executable;
    }

    public RuntimeStartResult Start(AgentDefinition agent, PersistedSession session)
    {
        var resume = TryResume(agent, session);
        if (resume.Attempted && resume.Available)
        {
            return new RuntimeStartResult(true, false);
        }

        StartNewConversation(agent, session);
        return new RuntimeStartResult(resume.Attempted, true);
    }

    public TmuxHealth GetStatus(AgentDefinition agent, PersistedSession session) =>
        tmux.GetHealth(session.TmuxSessionName, executable);

    public RuntimeResumeResult TryResume(AgentDefinition agent, PersistedSession session)
    {
        if (string.IsNullOrWhiteSpace(session.NativeConversationReference))
        {
            return new RuntimeResumeResult(false, false);
        }

        try
        {
            tmux.LaunchProcess(
                session.TmuxSessionName,
                agent.WorkingDirectory,
                executable,
                ["resume", session.NativeConversationReference]);
            return new RuntimeResumeResult(true, true);
        }
        catch (TmuxOperationException)
        {
            return new RuntimeResumeResult(true, false);
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
