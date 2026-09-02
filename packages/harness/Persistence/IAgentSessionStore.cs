using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness.Persistence;

public interface IAgentSessionStore
{
    AgentStatus EnsureAgent(AgentDefinition definition);

    AgentStatus ReadStatus(AgentDefinition definition);

    void SetDesiredState(string agentId, AgentDesiredState desiredState);

    AgentStatus RecordObservation(
        AgentDefinition definition,
        SessionObservedState observedState,
        string? lastError,
        ActivityEvent? activityEvent,
        string? nativeConversationReference = null,
        AgentDesiredState? desiredState = null,
        bool clearNativeConversationReference = false);

    void RecordConversationReference(string agentId, string reference);
}
