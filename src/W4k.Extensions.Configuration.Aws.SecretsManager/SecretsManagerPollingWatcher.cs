using System.Diagnostics.CodeAnalysis;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// AWS Secrets Manager configuration watcher that polls for changes.
/// </summary>
public sealed class SecretsManagerPollingWatcher : IConfigurationWatcher, IDisposable, IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeProvider _clock;

    private ITimer? _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerPollingWatcher"/> class.
    /// </summary>
    /// <param name="interval">Polling interval.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public SecretsManagerPollingWatcher(TimeSpan interval)
        : this(interval, TimeProvider.System)
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
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _interval = interval;
        _clock = timeProvider;
    }

    /// <inheritdoc/>
    public void Dispose() => _timer?.Dispose();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Thrown when watcher is already started.</exception>
    public void StartWatching(ISecretsManagerConfigurationProvider provider)
    {
        ThrowIfStarted(_timer);
        _timer = _clock.CreateTimer(ExecuteReload, provider, _interval, _interval);
    }

    /// <inheritdoc/>
    public void StopWatching()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _timer.Dispose();

        _timer = null;
    }

    private static void ExecuteReload(object? state)
    {
        var provider = (ISecretsManagerConfigurationProvider)state!;
        provider.Reload();
    }

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