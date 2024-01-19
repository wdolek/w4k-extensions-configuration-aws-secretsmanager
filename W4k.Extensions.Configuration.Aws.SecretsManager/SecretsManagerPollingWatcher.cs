using System.Diagnostics.CodeAnalysis;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// AWS Secrets Manager configuration watcher that polls for changes.
/// </summary>
public sealed class SecretsManagerPollingWatcher : IConfigurationWatcher, IDisposable, IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private Timer? _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerPollingWatcher"/> class.
    /// </summary>
    /// <param name="interval">Polling interval.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public SecretsManagerPollingWatcher(TimeSpan interval)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
#else
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }
#endif
        _interval = interval;
    }

    /// <inheritdoc />
    public void Dispose() => _timer?.Dispose();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_timer != null)
        {
            await _timer.DisposeAsync();
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when watcher is already started.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refresher"/> is <see langword="null"/>.</exception>
    public void Start(IConfigurationRefresher refresher)
    {
        ThrowIfStarted(_timer);
        ArgumentNullException.ThrowIfNull(refresher);

        _timer = new Timer(ExecuteRefresh, refresher, _interval, _interval);
    }

    [SuppressMessage("ReSharper", "ExplicitCallerInfoArgument", Justification = "Explicitly passing activity name")]
    private static void ExecuteRefresh(object? state)
    {
        var refresher = (IConfigurationRefresher)state!;
        try
        {
            refresher.RefreshAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            // no-op
        }
    }
    
    private static void ThrowIfStarted(Timer? timer)
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