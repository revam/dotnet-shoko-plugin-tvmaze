using System;
using System.Threading.Tasks;
using Shoko.Plugin.TvMaze.Client;
using Xunit;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Tests for the token bucket backing <see cref="TvMazeClient"/>'s calls,
/// which exists to keep this plugin inside TVmaze's documented "at least 20
/// calls every 10 seconds per IP" limit.
/// </summary>
public class TvMazeRateLimiterTests
{
    private static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Starts_full()
    {
        var clock = Clock();
        using var limiter = new TvMazeRateLimiter(maxTokens: 20, tokensPerSecond: 2, timeProvider: clock);

        Assert.Equal(20, limiter.AvailableTokens);
    }

    [Fact]
    public async Task A_burst_up_to_capacity_never_waits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = Clock();
        using var limiter = new TvMazeRateLimiter(maxTokens: 20, tokensPerSecond: 2, timeProvider: clock);

        for (var i = 0; i < 20; i++)
        {
            var waitTask = limiter.WaitAsync(cancellationToken);
            Assert.True(waitTask.IsCompletedSuccessfully, $"acquisition {i} should not have needed to wait");
            await waitTask;
            limiter.Release();
        }
    }

    [Fact]
    public async Task Consuming_a_token_reduces_the_balance_by_one()
    {
        var clock = Clock();
        using var limiter = new TvMazeRateLimiter(maxTokens: 5, tokensPerSecond: 1, timeProvider: clock);

        await limiter.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, limiter.AvailableTokens);
    }

    [Fact]
    public async Task Tokens_refill_over_time_but_never_past_the_cap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = Clock();
        using var limiter = new TvMazeRateLimiter(maxTokens: 20, tokensPerSecond: 2, timeProvider: clock);

        // Drain the bucket completely.
        for (var i = 0; i < 20; i++)
            await limiter.WaitAsync(cancellationToken);

        Assert.Equal(0, limiter.AvailableTokens, precision: 5);

        // Half TVmaze's window: 5 seconds at 2/s should refill 10 tokens.
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(10, limiter.AvailableTokens, precision: 5);

        // A long idle period saturates at the cap rather than overflowing.
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(20, limiter.AvailableTokens, precision: 5);
    }

    [Fact]
    public async Task Exhausting_the_bucket_makes_the_next_acquisition_wait_for_a_refill()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // A tight bucket so the wait the test asserts on is milliseconds, not
        // TVmaze's real 10-second window.
        var clock = Clock();
        using var limiter = new TvMazeRateLimiter(maxTokens: 1, tokensPerSecond: 20, timeProvider: clock);

        await limiter.WaitAsync(cancellationToken);
        limiter.Release();

        Assert.Equal(0, limiter.AvailableTokens, precision: 5);

        // The real clock is what WaitAsync's retry loop polls against, so
        // advancing the fake one lets the pending wait resolve without
        // the test itself sleeping for TVmaze's real window.
        var waitTask = limiter.WaitAsync(cancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
        Assert.Same(waitTask, completed);
        limiter.Release();
    }

    [Fact]
    public async Task Release_must_be_called_once_per_successful_wait()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = Clock();
        using var limiter = new TvMazeRateLimiter(maxTokens: 1, tokensPerSecond: 1, timeProvider: clock);

        await limiter.WaitAsync(cancellationToken);

        // The concurrency slot is still held until Release() is called, even
        // though tokens themselves may have refilled.
        clock.Advance(TimeSpan.FromSeconds(10));
        var second = limiter.WaitAsync(cancellationToken);
        await Task.Delay(50, cancellationToken);
        Assert.False(second.IsCompleted);

        limiter.Release();
        await second;
        limiter.Release();
    }
}
