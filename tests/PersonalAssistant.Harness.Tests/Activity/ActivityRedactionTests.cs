using PersonalAssistant.Harness.Activity;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Activity;

public sealed class ActivityRedactionTests
{
    [Fact]
    public void Redaction_redacts_nested_sensitive_values_and_unknown_keys()
    {
        var redacted = ActivityRedaction.RedactMetadata("""
            {
              "eventType": "terminal.input",
              "payload": {
                "token": "nested-secret",
                "path": "/Users/me/runtime/agents/personal/terminal/active.log"
              },
              "input": "secret keystrokes"
            }
            """);

        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-secret", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret keystrokes", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/me", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_redacts_sensitive_target_values()
    {
        var publicEvent = ActivityRedaction.ToPublicEvent(new ActivityEvent(
            "event",
            DateTimeOffset.UtcNow,
            "personal",
            "personal",
            "sessions",
            "terminal_hydration",
            "/Users/me/runtime/agents/personal/terminal/active.log",
            "success",
            null,
            """{"eventType":"terminal.hydration","outcome":"hydrated"}"""));

        Assert.Equal("[redacted]", publicEvent.Target);
    }
}
