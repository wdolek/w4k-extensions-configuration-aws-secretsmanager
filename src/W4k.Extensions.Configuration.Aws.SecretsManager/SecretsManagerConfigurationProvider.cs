using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// AWS Secrets Manager configuration provider.
/// </summary>
/// <remarks>
/// Disposing the provider (which configuration roots do on their own disposal) stops the configuration watcher.
/// Watcher created by the library is also disposed, watcher supplied by the caller is only stopped.
/// </remarks>
public sealed class SecretsManagerConfigurationProvider : ConfigurationProvider, ISecretsManagerConfigurationProvider, IDisposable
{
    private const string SecretIdTagName = "aws.secretsmanager.secret.id";
    private const string VersionIdTagName = "aws.secretsmanager.secret.version_id";

    // watcher lifecycle: Idle -> Starting -> Started -> Released, or Idle -> Released
    private const int WatcherIdle = 0;
    private const int WatcherStarting = 1;
    private const int WatcherStarted = 2;
    private const int WatcherReleased = 3;

    private readonly SecretFetcher _secretFetcher;
    private readonly IConfigurationWatcher? _watcher;
    private readonly bool _ownsWatcher;

    // has no timer and is cancelled but never disposed, reading its token can therefore never race Dispose()
    private readonly CancellationTokenSource _disposeCts = new();

    // makes "publish data" and "mark disposed" mutually exclusive; nothing else runs under it
    private readonly Lock _publishLock = new();

    private int _reloadInProgress;
    private int _watcherState;
    private int _disposed;
    private string? _currentSecretVersionId;
    private long _lastLoadedUtcTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerConfigurationProvider"/> class.
    /// </summary>
    /// <param name="source">The <see cref="SecretsManagerConfigurationSource"/>.</param>
    public SecretsManagerConfigurationProvider(SecretsManagerConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _secretFetcher = new SecretFetcher(source.SecretsManager);
        _watcher = source.ConfigurationWatcher;
        _ownsWatcher = source.OwnsConfigurationWatcher;
        Source = source;
    }

    /// <summary>
    /// Gets associated <see cref="SecretsManagerConfigurationSource"/>.
    /// </summary>
    public SecretsManagerConfigurationSource Source { get; }

    /// <summary>
    /// Gets version id of the last successfully loaded secret,
    /// <see langword="null"/> when the secret has not been loaded yet.
    /// </summary>
    public string? CurrentVersionId => Volatile.Read(ref _currentSecretVersionId);

    /// <summary>
    /// Gets UTC timestamp of the last successful load,
    /// <see langword="null"/> when the secret has not been loaded yet.
    /// </summary>
    /// <remarks>
    /// A skipped reload (secret version unchanged) does not update the timestamp.
    /// </remarks>
    public DateTimeOffset? LastLoadedAt
    {
        get
        {
            var utcTicks = Volatile.Read(ref _lastLoadedUtcTicks);
            return utcTicks == 0
                ? null
                : new DateTimeOffset(utcTicks, TimeSpan.Zero);
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name}: {Source.SecretName}";

    /// <summary>
    /// Stops the configuration watcher and cancels secret fetch in progress.
    /// </summary>
    /// <remarks>
    /// Disposal is final and idempotent. The watcher is stopped only when the provider has started it,
    /// and disposed only when it has been created by the library (see <see cref="SecretsManagerConfigurationBuilder.WithPollingWatcher(TimeSpan)"/>).
    /// A load or reload in progress is not awaited.
    /// </remarks>
    public void Dispose()
    {
        lock (_publishLock)
        {
            if (_disposed != 0)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
        }

        try
        {
            // abort an in-flight fetch first: a custom StopWatching() may join a worker blocked in that fetch
            _disposeCts.Cancel();
        }
        finally
        {
            ReleaseWatcher();
        }
    }

    /// <inheritdoc cref="ConfigurationProvider.Load"/>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    public override void Load()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var secretName = Source.SecretName;
        var secretVersion = Source.Version;
        var secretProcessor = Source.Processor;

        var secretIdTag = new KeyValuePair<string, object?>(SecretIdTagName, secretName);

        Activity? activity = null;
        ILogger? logger = null;
        var published = false;
        var discarded = false;

        try
        {
            activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.LoadActivityName);
            activity?.SetTag(SecretIdTagName, secretName);

            MeterDescriptors.Loads.Add(1, secretIdTag);

            logger = Source.LoggerFactory.CreateLogger<SecretsManagerConfigurationProvider>();

            using var cts = CreateFetchCancellation();
            var secret = Task
                .Run(() => _secretFetcher.GetSecret(secretName, secretVersion, cts.Token), cts.Token)
                .GetAwaiter()
                .GetResult();

            activity?.SetTag(VersionIdTagName, secret.VersionId);

            published = TryPublish(
                versionId: secret.VersionId,
                data: secretProcessor.GetConfigurationData(Source, secret.Value));

            if (!published)
            {
                discarded = true;
                activity?.AddEvent(new ActivityEvent("disposed"));
            }
            else
            {
                OnReload();

                if (!IsDisposed)
                {
                    activity?
                        .AddEvent(new ActivityEvent("loaded"))
                        .SetStatus(ActivityStatusCode.Ok, "Secret loaded");

                    logger.SecretLoaded(secretName, secret.VersionId);
                }

                // requires initial load to succeed (even when secret is optional)
                StartWatcherOnce();
            }
        }
        catch (Exception) when (IsDisposed)
        {
            // late failure, nobody is interested anymore
            activity?.AddEvent(new ActivityEvent("disposed"));
            discarded = !published;
        }
        catch (Exception ex)
        {
            MeterDescriptors.LoadFailures.Add(1, secretIdTag);

            activity?
                .AddException(ex)
                .SetStatus(ActivityStatusCode.Error, "Error loading secret");

            logger?.FailedToLoadSecret(ex, secretName);
            HandleException(ex, Source.OnLoadException);
        }
        finally
        {
            DisposeActivity(activity);
        }

        ObjectDisposedException.ThrowIf(discarded || (!published && IsDisposed), this);
    }

    /// <inheritdoc/>
    public void Reload()
    {
        if (IsDisposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _reloadInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            ReloadCore();
        }
        finally
        {
            Interlocked.Exchange(ref _reloadInProgress, 0);
        }
    }

    private void ReloadCore()
    {
        var secretName = Source.SecretName;
        var secretVersion = Source.Version;
        var secretProcessor = Source.Processor;

        var secretIdTag = new KeyValuePair<string, object?>(SecretIdTagName, secretName);

        Activity? activity = null;
        ILogger? logger = null;

        try
        {
            activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.ReloadActivityName);
            activity?.SetTag(SecretIdTagName, secretName);

            logger = Source.LoggerFactory.CreateLogger<SecretsManagerConfigurationProvider>();

            using var cts = CreateFetchCancellation();
            var secret = Task
                .Run(() => _secretFetcher.GetSecret(secretName, secretVersion, cts.Token), cts.Token)
                .GetAwaiter()
                .GetResult();

            activity?.SetTag(VersionIdTagName, secret.VersionId);

            var currentVersionId = Volatile.Read(ref _currentSecretVersionId);
            if (string.Equals(secret.VersionId, currentVersionId, StringComparison.Ordinal))
            {
                if (IsDisposed)
                {
                    activity?.AddEvent(new ActivityEvent("disposed"));
                    return;
                }

                MeterDescriptors.ReloadsSkipped.Add(1, secretIdTag);

                activity?
                    .AddEvent(new ActivityEvent("skipped"))
                    .SetStatus(ActivityStatusCode.Ok, "Secret up-to-date");

                logger.SecretAlreadyLoaded(secretName, secret.VersionId);
                return;
            }

            var previousVersionId = currentVersionId;
            var published = TryPublish(
                versionId: secret.VersionId,
                data: secretProcessor.GetConfigurationData(Source, secret.Value));

            if (!published)
            {
                activity?.AddEvent(new ActivityEvent("disposed"));
                return;
            }

            OnReload();

            if (IsDisposed)
            {
                // a change callback disposed the provider: data is published, success diagnostics are skipped
                activity?.AddEvent(new ActivityEvent("disposed"));
                return;
            }

            MeterDescriptors.Reloads.Add(1, secretIdTag);

            activity?
                .AddEvent(new ActivityEvent("reloaded"))
                .SetStatus(ActivityStatusCode.Ok, "Secret reloaded");

            logger.SecretRefreshed(secretName, previousVersionId ?? "N/A", secret.VersionId);
        }
        catch (Exception) when (IsDisposed)
        {
            // late failure, nobody is interested anymore
            activity?.AddEvent(new ActivityEvent("disposed"));
        }
        catch (Exception ex)
        {
            MeterDescriptors.ReloadFailures.Add(1, secretIdTag);

            activity?
                .AddException(ex)
                .SetStatus(ActivityStatusCode.Error, "Error reloading secret");

            logger?.FailedToRefreshSecret(ex, secretName);
            HandleException(ex, Source.OnReloadException);
        }
        finally
        {
            DisposeActivity(activity);
        }
    }

    [StackTraceHidden]
    private void HandleException(Exception exception, Action<SecretsManagerExceptionContext>? callback)
    {
        var ignore = false;
        if (callback is not null)
        {
            var exceptionContext = new SecretsManagerExceptionContext(this, exception);

            callback(exceptionContext);
            ignore = exceptionContext.Ignore;
        }

        if (!ignore)
        {
            var envelopeException = new SecretRetrievalException(
                $"Failed to fetch secret '{Source.SecretName}'",
                Source.SecretName,
                exception);

            var exceptionDispatchInfo = ExceptionDispatchInfo.Capture(envelopeException);

            exceptionDispatchInfo.Throw();
        }
    }

    // publishes data unless disposed; never calls OnReload() itself
    private bool TryPublish(string versionId, Dictionary<string, string?> data)
    {
        lock (_publishLock)
        {
            if (_disposed != 0)
            {
                return false;
            }

            Volatile.Write(ref _currentSecretVersionId, versionId);
            Volatile.Write(ref _lastLoadedUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
            Data = data;

            return true;
        }
    }

    private CancellationTokenSource CreateFetchCancellation()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        try
        {
            // throws for timeouts beyond ~49.7 days
            cts.CancelAfter(Source.Timeout);
        }
        catch
        {
            // unregister from _disposeCts before rethrowing
            cts.Dispose();
            throw;
        }

        return cts;
    }

    // Activity.Dispose() runs ActivityStopped listeners, which is consumer code
    private void DisposeActivity(Activity? activity)
    {
        try
        {
            activity?.Dispose();
        }
        catch (Exception) when (IsDisposed)
        {
            // late failure, nobody is interested anymore
        }
    }

    private void StartWatcherOnce()
    {
        if (_watcher is null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _watcherState, WatcherStarting, WatcherIdle) != WatcherIdle)
        {
            return;
        }

        try
        {
            _watcher.StartWatching(this);
        }
        catch
        {
            // not started: do not stop it, the failure may mean it belongs to another provider
            Interlocked.CompareExchange(ref _watcherState, WatcherIdle, WatcherStarting);
            if (IsDisposed)
            {
                ReleaseWatcher();
            }

            throw;
        }

        Interlocked.CompareExchange(ref _watcherState, WatcherStarted, WatcherStarting);

        if (IsDisposed)
        {
            // disposal raced the start
            ReleaseWatcher();
        }
    }

    // exactly one caller wins the transition to Released, so the watcher is stopped and disposed at most once
    private void ReleaseWatcher()
    {
        while (true)
        {
            switch (Volatile.Read(ref _watcherState))
            {
                case WatcherIdle:
                    if (Interlocked.CompareExchange(ref _watcherState, WatcherReleased, WatcherIdle) == WatcherIdle)
                    {
                        DisposeOwnedWatcher();
                        return;
                    }

                    break;

                case WatcherStarted:
                    if (Interlocked.CompareExchange(ref _watcherState, WatcherReleased, WatcherStarted) == WatcherStarted)
                    {
                        try
                        {
                            _watcher!.StopWatching();
                        }
                        finally
                        {
                            DisposeOwnedWatcher();
                        }

                        return;
                    }

                    break;

                default:
                    // Starting: the starting thread releases the watcher once StartWatching returns
                    // Released: already done
                    return;
            }
        }
    }

    private void DisposeOwnedWatcher()
    {
        if (_ownsWatcher && _watcher is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

internal static partial class LoggerExtensions
{
    [LoggerMessage(555_2368_11, LogLevel.Information, "Secret {SecretName}:{VersionId} has been loaded", EventName = "SecretLoaded")]
    public static partial void SecretLoaded(this ILogger logger, string secretName, string versionId);

    [LoggerMessage(555_2368_10, LogLevel.Error, "Failed to load secret {SecretName}", EventName = "FailedToLoadSecret")]
    public static partial void FailedToLoadSecret(this ILogger logger, Exception exception, string secretName);

    [LoggerMessage(555_2368_22, LogLevel.Information, "Secret {SecretName}:{VersionId} is already loaded, skipping", EventName = "SecretAlreadyLoaded")]
    public static partial void SecretAlreadyLoaded(this ILogger logger, string secretName, string versionId);

    [LoggerMessage(555_2368_21, LogLevel.Information, "Secret {SecretName}:{PreviousVersionId}->{VersionId} has been reloaded", EventName = "SecretReloaded")]
    public static partial void SecretRefreshed(this ILogger logger, string secretName, string previousVersionId, string versionId);

    [LoggerMessage(555_2368_20, LogLevel.Error, "Failed to reload secret {SecretName}", EventName = "FailedToReloadSecret")]
    public static partial void FailedToRefreshSecret(this ILogger logger, Exception exception, string secretName);
}
