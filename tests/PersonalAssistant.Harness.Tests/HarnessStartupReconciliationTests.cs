using PersonalAssistant.Harness.Agents;
using Xunit;

namespace PersonalAssistant.Harness.Tests;

public sealed class HarnessStartupReconciliationTests
{
    [Fact]
    public void ReconcileReviewedAgents_reconciles_personal_and_work_agents()
    {
        var service = new RecordingAgentSessionService();

        HarnessStartupReconciliation.ReconcileReviewedAgents(service);

        Assert.Equal(["personal", "work"], service.ReconciledAgentIds);
    }

    private sealed class RecordingAgentSessionService : IAgentSessionService
    {
        public List<string> ReconciledAgentIds { get; } = [];

        public AgentStatus GetPersonal() => throw new NotSupportedException();
        public AgentStatus ReconcilePersonal()
        {
            ReconciledAgentIds.Add("personal");
            return default!;
        }

        public AgentStatus StartPersonal() => throw new NotSupportedException();
        public AgentStatus StopPersonal() => throw new NotSupportedException();
        public void RecordPersonalConversationReference(string reference) => throw new NotSupportedException();
        public AgentStatus GetWork() => throw new NotSupportedException();

        public AgentStatus ReconcileWork()
        {
            ReconciledAgentIds.Add("work");
            return default!;
        }

        public AgentStatus StartWork() => throw new NotSupportedException();
        public AgentStatus StopWork() => throw new NotSupportedException();
        public void RecordWorkConversationReference(string reference) => throw new NotSupportedException();
    }
}
