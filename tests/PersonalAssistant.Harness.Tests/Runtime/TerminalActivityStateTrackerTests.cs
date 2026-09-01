using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class TerminalActivityStateTrackerTests
{
    [Fact]
    public async Task Starts_healthy_sessions_idle_and_emits_only_explicit_state_transitions()
    {
        using var tracker = new TerminalActivityStateTracker("personal");
        using var subscription = tracker.Subscribe();

        Assert.Equal(TerminalActivityState.Idle, await ReadOne(subscription));
        tracker.MarkBusy();
        tracker.MarkWaiting();
        tracker.MarkError();
        tracker.MarkIdle();

        Assert.Equal(
            [
                TerminalActivityState.Busy,
                TerminalActivityState.Waiting,
                TerminalActivityState.Error,
                TerminalActivityState.Idle
            ],
            [
                await ReadOne(subscription),
                await ReadOne(subscription),
                await ReadOne(subscription),
                await ReadOne(subscription)
            ]);
    }

    [Fact]
    public async Task Repeating_a_state_does_not_create_duplicate_events()
    {
        using var tracker = new TerminalActivityStateTracker("personal");
        using var subscription = tracker.Subscribe();

        Assert.Equal(TerminalActivityState.Idle, await ReadOne(subscription));
        tracker.MarkIdle();

        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public async Task A_healthy_reconnect_can_reset_a_previous_error_to_idle()
    {
        using var tracker = new TerminalActivityStateTracker("personal");
        using var subscription = tracker.Subscribe();

        Assert.Equal(TerminalActivityState.Idle, await ReadOne(subscription));
        tracker.MarkError();
        Assert.Equal(TerminalActivityState.Error, await ReadOne(subscription));
        tracker.ResetIfError();

        Assert.Equal(TerminalActivityState.Idle, await ReadOne(subscription));
    }

    private static async Task<TerminalActivityState> ReadOne(TerminalActivityStateSubscription subscription)
    {
        Assert.True(await subscription.Reader.WaitToReadAsync());
        Assert.True(subscription.Reader.TryRead(out var state));
        return state;
    }
}
