using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class TerminalInputSerializerTests
{
    [Fact]
    public async Task Delivers_interleaved_submissions_in_fifo_order_with_one_in_flight_operation()
    {
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new List<long>();
        using var serializer = new TerminalInputSerializer(
            "personal",
            async (request, cancellationToken) =>
            {
                delivered.Add(request.Sequence);
                if (request.Sequence == 1)
                {
                    firstStarted.SetResult(true);
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
            },
            queueCapacity: 2);

        var first = serializer.EnqueueAsync(1, "first");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = serializer.EnqueueAsync(2, "second");
        var third = serializer.EnqueueAsync(3, "third");

        Assert.True(serializer.HasInFlightOperation);
        Assert.Equal(2, serializer.QueuedCount);
        releaseFirst.SetResult(true);

        var acknowledgements = await Task.WhenAll(first, second, third);
        Assert.Equal([1L, 2L, 3L], acknowledgements.Select(item => item.Sequence));
        Assert.Equal([1L, 2L, 3L], delivered);
    }

    [Fact]
    public async Task Rejects_new_input_when_the_bounded_queue_is_full()
    {
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var serializer = new TerminalInputSerializer(
            "personal",
            async (request, cancellationToken) =>
            {
                if (request.Sequence == 1)
                {
                    firstStarted.SetResult(true);
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
            },
            queueCapacity: 1);

        var first = serializer.EnqueueAsync(1, "first");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = serializer.EnqueueAsync(2, "second");

        var exception = await Assert.ThrowsAsync<TerminalInputException>(() => serializer.EnqueueAsync(3, "third"));
        Assert.Equal("terminal_input_queue_full", exception.Code);
        releaseFirst.SetResult(true);
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Cancellation_removes_queued_input_without_delivering_it()
    {
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new List<long>();
        using var serializer = new TerminalInputSerializer(
            "personal",
            async (request, cancellationToken) =>
            {
                delivered.Add(request.Sequence);
                if (request.Sequence == 1)
                {
                    firstStarted.SetResult(true);
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
            },
            queueCapacity: 2);

        var first = serializer.EnqueueAsync(1, "first");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        var cancelled = serializer.EnqueueAsync(2, "cancelled", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        releaseFirst.SetResult(true);
        await first;
        await WaitForAsync(() => serializer.QueuedCount == 0);

        Assert.Equal([1L], delivered);
    }

    [Fact]
    public async Task Cancellation_reaches_an_in_flight_operation_and_releases_the_worker()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var quiescent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var serializer = new TerminalInputSerializer(
            "personal",
            async (_, cancellationToken) =>
            {
                started.SetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled.SetResult(true);
                    throw;
                }
            });
        serializer.BecameQuiescent += () => quiescent.TrySetResult(true);
        using var cancellation = new CancellationTokenSource();

        var input = serializer.EnqueueAsync(1, "in flight", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => input);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await quiescent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForAsync(() => !serializer.HasInFlightOperation);
    }

    [Fact]
    public async Task Dispose_cancels_an_in_flight_operation_before_returning()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serializer = new TerminalInputSerializer(
            "personal",
            async (_, cancellationToken) =>
            {
                started.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        var input = serializer.EnqueueAsync(1, "shutdown");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        serializer.Dispose();

        var exception = await Assert.ThrowsAsync<TerminalInputException>(() => input);
        Assert.Equal("terminal_input_unavailable", exception.Code);
    }

    [Fact]
    public async Task Rejects_invalid_or_oversized_frames_before_queueing()
    {
        using var serializer = new TerminalInputSerializer(
            "personal",
            (_, _) => Task.CompletedTask);

        var negativeSequence = await Assert.ThrowsAsync<TerminalInputException>(() => serializer.EnqueueAsync(-1, "data"));
        var empty = await Assert.ThrowsAsync<TerminalInputException>(() => serializer.EnqueueAsync(1, string.Empty));
        var oversized = await Assert.ThrowsAsync<TerminalInputException>(() =>
            serializer.EnqueueAsync(2, new string('x', TerminalProtocol.MaxPayloadBytes + 1)));

        Assert.Equal("terminal_input_sequence_invalid", negativeSequence.Code);
        Assert.Equal("terminal_input_empty", empty.Code);
        Assert.Equal("terminal_input_too_large", oversized.Code);
        Assert.Equal(0, serializer.QueuedCount);
    }

    [Fact]
    public async Task Converts_operation_failures_to_stable_privacy_safe_errors()
    {
        using var serializer = new TerminalInputSerializer(
            "personal",
            (_, _) => Task.FromException(new InvalidOperationException("secret input should not escape")));

        var exception = await Assert.ThrowsAsync<TerminalInputException>(() => serializer.EnqueueAsync(1, "secret"));

        Assert.Equal("terminal_input_failed", exception.Code);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(20, cancellation.Token);
        }
    }
}
