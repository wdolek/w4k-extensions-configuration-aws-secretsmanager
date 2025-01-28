using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// AWS Secrets Manager configuration provider.
/// </summary>
public sealed class SecretsManagerConfigurationProvider : ConfigurationProvider, ISecretsManagerConfigurationProvider
{
    private readonly SecretFetcher _secretFetcher;

    private int _reloadInProgress;
    private string? _currentSecretVersionId;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerConfigurationProvider"/> class.
    /// </summary>
    /// <param name="source">The <see cref="SecretsManagerConfigurationSource"/>.</param>
    public SecretsManagerConfigurationProvider(SecretsManagerConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _secretFetcher = new SecretFetcher(source.SecretsManager);
        Source = source;
    }

    /// <summary>
    /// Gets associated <see cref="SecretsManagerConfigurationSource"/>.
    /// </summary>
    public SecretsManagerConfigurationSource Source { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name}: {Source.SecretName}";

    /// <inheritdoc cref="ConfigurationProvider.Load"/>
    public override void Load()
    {
        var secretName = Source.SecretName;
        var secretVersion = Source.Version;
        var secretProcessor = Source.Processor;
        var watcher = Source.ConfigurationWatcher;

        var logger = Source.LoggerFactory.CreateLogger<SecretsManagerConfigurationProvider>();

        using var activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.LoadActivityName);
        try
        {
            var cts = new CancellationTokenSource(Source.Timeout);
            var secret = _secretFetcher.GetSecret(secretName, secretVersion, cts.Token)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            SetData(
                versionId: secret.VersionId,
                data: secretProcessor.GetConfigurationData(Source, secret.Value));

            activity?
                .AddEvent(new ActivityEvent("loaded"))
                .SetStatus(ActivityStatusCode.Ok, "Secret loaded");

            logger.SecretLoaded(secretName, secret.VersionId);

            // requires initial load to succeed (even when secret is optional)
            watcher?.StartWatching(this);
        }
        catch (Exception ex)
        {
#if NET9_0_OR_GREATER
            activity?
                .AddException(ex)
                .SetStatus(ActivityStatusCode.Error, "Error loading secret");
#else
            activity?
                .AddEvent(ex.ToActivityEvent())
                .SetStatus(ActivityStatusCode.Error, "Error loading secret");
#endif

            logger.FailedToLoadSecret(ex, secretName);
            HandleException(ex, Source.OnLoadException);
        }
    }

    /// <inheritdoc/>
    public void Reload()
    {
        if (Interlocked.Exchange(ref _reloadInProgress, 1) == 1)
        {
            return;
        }

        var secretName = Source.SecretName;
        var secretVersion = Source.Version;
        var secretProcessor = Source.Processor;

        var logger = Source.LoggerFactory.CreateLogger<SecretsManagerConfigurationProvider>();

        using var activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.ReloadActivityName);
        try
        {
            var cts = new CancellationTokenSource(Source.Timeout);
            var secret = _secretFetcher.GetSecret(secretName, secretVersion, cts.Token)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            if (string.Equals(secret.VersionId, _currentSecretVersionId, StringComparison.Ordinal))
            {
                activity?
                    .AddEvent(new ActivityEvent("skipped"))
                    .SetStatus(ActivityStatusCode.Ok, "Secret up-to-date");

                logger.SecretAlreadyLoaded(secretName, secret.VersionId);
                return;
            }

            var previousVersionId = _currentSecretVersionId;
            SetData(
                versionId: secret.VersionId,
                data: secretProcessor.GetConfigurationData(Source, secret.Value));

            activity?
                .AddEvent(new ActivityEvent("reloaded"))
                .SetStatus(ActivityStatusCode.Ok, "Secret reloaded");

            logger.SecretRefreshed(secretName, previousVersionId ?? "N/A", secret.VersionId);
        }
        catch (Exception ex)
        {
#if NET9_0_OR_GREATER
            activity?
                .AddException(ex)
                .SetStatus(ActivityStatusCode.Error, "Error reloading secret");
#else
            activity?
                .AddEvent(ex.ToActivityEvent())
                .SetStatus(ActivityStatusCode.Error, "Error reloading secret");
#endif

            logger.FailedToRefreshSecret(ex, secretName);
            HandleException(ex, Source.OnReloadException);
        }
        finally
        {
            Interlocked.Exchange(ref _reloadInProgress, 0);
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
            var envelopeException = new SecretRetrievalException("Failed to fetch secret", exception);
            var exceptionDispatchInfo = ExceptionDispatchInfo.Capture(envelopeException);

            exceptionDispatchInfo.Throw();
        }
    }

    private void SetData(string versionId, Dictionary<string, string?> data)
    {
        _currentSecretVersionId = versionId;
        Data = data;

        OnReload();
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