using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class TerminalOutputHubTests
{
    [Fact]
    public async Task Subscribers_receive_shared_monotonic_output_sequences()
    {
        using var hub = new TerminalOutputHub();
        using var first = hub.Subscribe(capacity: 4);
        using var second = hub.Subscribe(capacity: 4);

        Assert.True(hub.Publish("one"));
        Assert.True(hub.Publish("two"));

        Assert.Equal(new TerminalOutputMessage(1, "one"), await ReadOne(first));
        Assert.Equal(new TerminalOutputMessage(2, "two"), await ReadOne(first));
        Assert.Equal(new TerminalOutputMessage(1, "one"), await ReadOne(second));
        Assert.Equal(new TerminalOutputMessage(2, "two"), await ReadOne(second));
    }

    [Fact]
    public async Task A_slow_observer_is_closed_with_a_stable_error_without_blocking_publish()
    {
        using var hub = new TerminalOutputHub();
        using var subscription = hub.Subscribe(capacity: 1);

        Assert.True(hub.Publish("first"));
        Assert.False(hub.Publish("second"));
        Assert.Equal(0, hub.ObserverCount);
        Assert.Equal(new TerminalOutputMessage(1, "first"), await ReadOne(subscription));

        var exception = await Assert.ThrowsAsync<TerminalStreamException>(async () =>
        {
            await subscription.Reader.WaitToReadAsync();
        });
        Assert.Equal("terminal_client_slow", exception.Code);
    }

    [Fact]
    public async Task Disposing_a_subscription_cancels_only_that_observer()
    {
        using var hub = new TerminalOutputHub();
        using var first = hub.Subscribe(capacity: 2);
        using var second = hub.Subscribe(capacity: 2);

        first.Dispose();
        Assert.Equal(1, hub.ObserverCount);
        Assert.True(hub.Publish("remaining"));

        Assert.Equal(new TerminalOutputMessage(1, "remaining"), await ReadOne(second));
    }

    [Fact]
    public async Task A_new_observer_starts_at_the_current_sequence_boundary()
    {
        using var hub = new TerminalOutputHub();
        using var first = hub.Subscribe(capacity: 2);
        Assert.True(hub.Publish("before observer"));
        using var second = hub.SubscribeAfterCurrent(capacity: 2);
        Assert.True(hub.Publish("after observer"));

        Assert.Equal(new TerminalOutputMessage(1, "before observer"), await ReadOne(first));
        Assert.Equal(new TerminalOutputMessage(2, "after observer"), await ReadOne(first));
        Assert.Equal(new TerminalOutputMessage(2, "after observer"), await ReadOne(second));
    }

    private static async Task<TerminalOutputMessage> ReadOne(TerminalOutputSubscription subscription)
    {
        Assert.True(await subscription.Reader.WaitToReadAsync());
        Assert.True(subscription.Reader.TryRead(out var message));
        return message;
    }
}
