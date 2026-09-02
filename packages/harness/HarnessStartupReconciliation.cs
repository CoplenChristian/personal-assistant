using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness;

public static class HarnessStartupReconciliation
{
    public static void ReconcileReviewedAgents(IAgentSessionService agents)
    {
        agents.ReconcilePersonal();
        agents.ReconcileWork();
    }
}
