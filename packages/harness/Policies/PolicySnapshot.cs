namespace PersonalAssistant.Harness.Policies;

public sealed record PolicySnapshot(
    bool EmailSendingDisabled,
    bool UnverifiedMessageRecipientsBlocked,
    bool GroupMessagingDisabled,
    bool CrossRealmFallbackDenied,
    bool ConsequentialAuditRequired,
    bool CheckpointBeforeRotationRequired);

public sealed class PolicyConfigurationException(string message) : Exception(message);
