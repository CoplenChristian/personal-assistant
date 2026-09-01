using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PersonalAssistant.Harness.Policies;

public static class PolicyLoader
{
    public static PolicySnapshot Load(string capabilityPolicyPath, string realmPolicyPath, bool checkpointBeforeRotation)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var capability = deserializer.Deserialize<CapabilityPolicyDocument>(File.ReadAllText(capabilityPolicyPath))
                ?? throw new PolicyConfigurationException("capability-policy.yaml is empty.");
            var realm = deserializer.Deserialize<RealmPolicyDocument>(File.ReadAllText(realmPolicyPath))
                ?? throw new PolicyConfigurationException("realm-policy.yaml is empty.");

            var emailSendingDisabled = capability.Mail.DeniedOperations.Contains("send", StringComparer.OrdinalIgnoreCase)
                && !capability.Mail.AllowedOperations.Contains("send", StringComparer.OrdinalIgnoreCase);
            var recipientsBlocked = capability.Messaging.RequireVerifiedContact;
            var groupsDisabled = !capability.Messaging.AllowGroups;
            var crossRealmDenied = string.Equals(capability.Realms.CrossRealmAccess, "deny", StringComparison.OrdinalIgnoreCase)
                && string.Equals(realm.Default, "deny", StringComparison.OrdinalIgnoreCase);

            if (!emailSendingDisabled || !recipientsBlocked || !groupsDisabled || !crossRealmDenied
                || !capability.Audit.ConsequentialActionsAreImmutable
                || !capability.Audit.RecordBlockedSecurityActions
                || !checkpointBeforeRotation)
            {
                throw new PolicyConfigurationException("Repository policy defaults do not satisfy the hard safety invariants.");
            }

            return new PolicySnapshot(true, true, true, true, true, true);
        }
        catch (PolicyConfigurationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PolicyConfigurationException($"Unable to load policy defaults: {exception.Message}");
        }
    }

    private sealed class CapabilityPolicyDocument
    {
        public MailPolicy Mail { get; set; } = new();
        public MessagingPolicy Messaging { get; set; } = new();
        public RealmCapabilityPolicy Realms { get; set; } = new();
        public AuditPolicy Audit { get; set; } = new();
    }

    private sealed class MailPolicy
    {
        public List<string> AllowedOperations { get; set; } = [];
        public List<string> DeniedOperations { get; set; } = [];
    }

    private sealed class MessagingPolicy
    {
        public bool RequireVerifiedContact { get; set; }
        public bool AllowGroups { get; set; }
    }

    private sealed class RealmCapabilityPolicy { public string CrossRealmAccess { get; set; } = string.Empty; }

    private sealed class AuditPolicy
    {
        public bool ConsequentialActionsAreImmutable { get; set; }
        public bool RecordBlockedSecurityActions { get; set; }
    }

    private sealed class RealmPolicyDocument
    {
        public string Default { get; set; } = string.Empty;
    }
}
