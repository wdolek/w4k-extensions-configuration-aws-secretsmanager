using System.Diagnostics.CodeAnalysis;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// AWS Secrets Manager configuration watcher that polls for changes.
/// </summary>
public sealed class SecretsManagerPollingWatcher : IConfigurationWatcher, IDisposable, IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _maxJitter;
    private readonly TimeProvider _clock;

    private readonly Lock _lock = new();

    // _timer and _disposed are only read or written under _lock
    private ITimer? _timer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerPollingWatcher"/> class.
    /// </summary>
    /// <param name="interval">Polling interval.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public SecretsManagerPollingWatcher(TimeSpan interval)
        : this(interval, TimeSpan.Zero, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerPollingWatcher"/> class.
    /// </summary>
    /// <param name="interval">Polling interval.</param>
    /// <param name="maxJitter">Maximum positive jitter applied to the polling interval.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is less or equal to <see cref="TimeSpan.Zero"/> or <paramref name="maxJitter"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    public SecretsManagerPollingWatcher(TimeSpan interval, TimeSpan maxJitter)
        : this(interval, maxJitter, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerPollingWatcher"/> class.
    /// </summary>
    /// <param name="interval">Polling interval.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public SecretsManagerPollingWatcher(TimeSpan interval, TimeProvider timeProvider)
        : this(interval, TimeSpan.Zero, timeProvider)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerPollingWatcher"/> class.
    /// </summary>
    /// <param name="interval">Polling interval.</param>
    /// <param name="maxJitter">Maximum positive jitter applied to the polling interval.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is less or equal to <see cref="TimeSpan.Zero"/> or <paramref name="maxJitter"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public SecretsManagerPollingWatcher(TimeSpan interval, TimeSpan maxJitter, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxJitter, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _interval = interval;
        _maxJitter = maxJitter;
        _clock = timeProvider;
    }

    /// <inheritdoc/>
    public void Dispose() => DetachTimer(markDisposed: true)?.Dispose();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() =>
        DetachTimer(markDisposed: true)?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when watcher is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when watcher is already started.</exception>
    public void StartWatching(ISecretsManagerConfigurationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfStarted(_timer);

            _timer = _clock.CreateTimer(ExecuteReload, provider, GetNextInterval(), Timeout.InfiniteTimeSpan);
        }
    }

    /// <inheritdoc/>
    public void StopWatching() => DetachTimer(markDisposed: false)?.Dispose();

    private ITimer? DetachTimer(bool markDisposed)
    {
        lock (_lock)
        {
            _disposed |= markDisposed;

            var timer = _timer;
            _timer = null;

            return timer;
        }
    }

    private void ExecuteReload(object? state)
    {
        var provider = (ISecretsManagerConfigurationProvider)state!;
        try
        {
            provider.Reload();
        }
        finally
        {
            Reschedule();
        }
    }

    private void Reschedule()
    {
        ITimer? timer;
        lock (_lock)
        {
            timer = _timer;
        }

        try
        {
            // a stop detaching this timer in the meantime disposes it right after, so re-arming cannot outlive the stop
            timer?.Change(GetNextInterval(), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private TimeSpan GetNextInterval() => _maxJitter > TimeSpan.Zero
        ? _interval + (Random.Shared.NextDouble() * _maxJitter)
        : _interval;

    private static void ThrowIfStarted(ITimer? timer)
    {
        if (timer is not null)
        {
            ThrowInvalidOperationNotStarted();
        }
    }

    [DoesNotReturn]
    private static void ThrowInvalidOperationNotStarted() =>
        throw new InvalidOperationException("Watcher is already started, have you re-used watcher instance?");
}