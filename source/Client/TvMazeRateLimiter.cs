using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shoko.Plugin.TvMaze.Client;

/// <summary>
/// Token bucket rate limiter for TVmaze's documented limit of at least 20
/// calls every 10 seconds per IP address (see
/// <see href="https://www.tvmaze.com/api"/>). Defaults to a bucket of 20
/// tokens refilling at 2 per second, which averages to the documented limit
/// while still allowing a short burst. Thread-safe.
/// </summary>
public sealed class TvMazeRateLimiter : IDisposable
{
    private readonly int _maxTokens;
    private readonly double _tokensPerSecond;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly object _lock = new();

    private double _currentTokens;
    private DateTimeOffset _lastRefill;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeRateLimiter"/> class.
    /// </summary>
    /// <param name="maxTokens">Maximum burst capacity / max concurrent requests.</param>
    /// <param name="tokensPerSecond">Tokens refilled per second.</param>
    /// <param name="timeProvider">
    /// The time source to measure refills against. Defaults to
    /// <see cref="TimeProvider.System"/>; a test passes its own so refills can
    /// be simulated without a real wait.
    /// </param>
    public TvMazeRateLimiter(int maxTokens = 20, double tokensPerSecond = 2.0, TimeProvider? timeProvider = null)
    {
        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), maxTokens, "Must be positive.");
        if (tokensPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(tokensPerSecond), tokensPerSecond, "Must be positive.");

        _maxTokens = maxTokens;
        _tokensPerSecond = tokensPerSecond;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _concurrencySemaphore = new SemaphoreSlim(maxTokens, maxTokens);
        _currentTokens = maxTokens;
        _lastRefill = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Acquires a token, waiting until one is available. Callers must pair
    /// this with a <see cref="Release"/> call (e.g. try/finally).
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!TryConsumeToken())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _concurrencySemaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// Releases the concurrency slot acquired by <see cref="WaitAsync"/>. Must
    /// be called exactly once per successful <see cref="WaitAsync"/> call.
    /// </summary>
    public void Release() => _concurrencySemaphore.Release();

    /// <summary>
    /// The number of tokens currently available, after refilling for elapsed
    /// time. Exposed for tests; callers should use <see cref="WaitAsync"/>.
    /// </summary>
    internal double AvailableTokens
    {
        get
        {
            lock (_lock)
            {
                Refill();
                return _currentTokens;
            }
        }
    }

    private bool TryConsumeToken()
    {
        lock (_lock)
        {
            Refill();
            if (_currentTokens < 1)
                return false;

            _currentTokens--;
            return true;
        }
    }

    private void Refill()
    {
        var now = _timeProvider.GetUtcNow();
        var elapsedSeconds = (now - _lastRefill).TotalSeconds;
        if (elapsedSeconds <= 0)
            return;

        _currentTokens = Math.Min(_maxTokens, _currentTokens + elapsedSeconds * _tokensPerSecond);
        _lastRefill = now;
    }

    /// <inheritdoc/>
    public void Dispose() => _concurrencySemaphore.Dispose();
}
