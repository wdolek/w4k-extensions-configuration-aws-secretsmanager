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
    private const string SecretIdTagName = "aws.secretsmanager.secret.id";
    private const string SecretArnTagName = "aws.secretsmanager.secret.arn";
    private const string VersionIdTagName = "aws.secretsmanager.secret.version_id";

    private readonly SecretFetcher _secretFetcher;

    private int _reloadInProgress;
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

            return utcTicks == 0 ? null : new DateTimeOffset(utcTicks, TimeSpan.Zero);
        }
    }

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
        activity?.SetTag(SecretIdTagName, secretName);

        var secretIdTag = new KeyValuePair<string, object?>(SecretIdTagName, secretName);
        MeterDescriptors.Loads.Add(1, secretIdTag);

        try
        {
            using var cts = new CancellationTokenSource(Source.Timeout);
            var secret = Task
                .Run(() => _secretFetcher.GetSecret(secretName, secretVersion, cts.Token), cts.Token)
                .GetAwaiter()
                .GetResult();

            SetFetchedSecretTags(activity, secret);
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
            MeterDescriptors.LoadFailures.Add(1, secretIdTag);

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
        activity?.SetTag(SecretIdTagName, secretName);

        var secretIdTag = new KeyValuePair<string, object?>(SecretIdTagName, secretName);

        try
        {
            using var cts = new CancellationTokenSource(Source.Timeout);
            var secret = Task
                .Run(() => _secretFetcher.GetSecret(secretName, secretVersion, cts.Token), cts.Token)
                .GetAwaiter()
                .GetResult();

            SetFetchedSecretTags(activity, secret);

            var currentVersionId = Volatile.Read(ref _currentSecretVersionId);
            if (string.Equals(secret.VersionId, currentVersionId, StringComparison.Ordinal))
            {
                MeterDescriptors.ReloadsSkipped.Add(1, secretIdTag);

                activity?
                    .AddEvent(new ActivityEvent("skipped"))
                    .SetStatus(ActivityStatusCode.Ok, "Secret up-to-date");

                logger.SecretAlreadyLoaded(secretName, secret.VersionId);
                return;
            }

            var previousVersionId = currentVersionId;
            SetData(
                versionId: secret.VersionId,
                data: secretProcessor.GetConfigurationData(Source, secret.Value));

            MeterDescriptors.Reloads.Add(1, secretIdTag);

            activity?
                .AddEvent(new ActivityEvent("reloaded"))
                .SetStatus(ActivityStatusCode.Ok, "Secret reloaded");

            logger.SecretRefreshed(secretName, previousVersionId ?? "N/A", secret.VersionId);
        }
        catch (Exception ex)
        {
            MeterDescriptors.ReloadFailures.Add(1, secretIdTag);

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
            var envelopeException = new SecretRetrievalException(
                $"Failed to fetch secret '{Source.SecretName}'",
                Source.SecretName,
                exception);

            var exceptionDispatchInfo = ExceptionDispatchInfo.Capture(envelopeException);

            exceptionDispatchInfo.Throw();
        }
    }

    private void SetData(string versionId, Dictionary<string, string?> data)
    {
        Volatile.Write(ref _currentSecretVersionId, versionId);
        Volatile.Write(ref _lastLoadedUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        Data = data;

        OnReload();
    }

    private static void SetFetchedSecretTags(Activity? activity, SecretValue secret)
    {
        activity?.SetTag(VersionIdTagName, secret.VersionId);

        if (secret.Arn is not null)
        {
            activity?.SetTag(SecretArnTagName, secret.Arn);
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