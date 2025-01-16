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
    private readonly ILogger _logger;

    private int _refreshInProgress;
    private string? _currentSecretVersionId;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerConfigurationProvider"/> class.
    /// </summary>
    /// <param name="source">The <see cref="SecretsManagerConfigurationSource"/>.</param>
    public SecretsManagerConfigurationProvider(SecretsManagerConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;

        _secretFetcher = new SecretFetcher(source.SecretsManager);
        _logger = source.LoggerFactory.CreateLogger<SecretsManagerConfigurationProvider>();
    }

    /// <summary>
    /// Gets associated <see cref="SecretsManagerConfigurationSource"/>.
    /// </summary>
    public SecretsManagerConfigurationSource Source { get; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{GetType().Name}: {Source.SecretName} ({(Source.IsOptional ? "optional" : "required")})";

    /// <inheritdoc cref="ConfigurationProvider.Load"/>
    public override void Load()
    {
        var startingTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var cts = new CancellationTokenSource(Source.Timeout);
            LoadAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();

            Source.ConfigurationWatcher?.Start(this);
        }
        catch (Exception ex)
        {
            var elapsedTime = Stopwatch.GetElapsedTime(startingTimestamp);
            HandleException(ex, Source.OnLoadException, elapsedTime);
        }
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        var startingTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var cts = new CancellationTokenSource(Source.Timeout);
            RefreshAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var elapsedTime = Stopwatch.GetElapsedTime(startingTimestamp);
            HandleException(ex, Source.OnRefreshException, elapsedTime);
        }
    }

    [StackTraceHidden]
    private void HandleException(Exception exception, Action<SecretsManagerExceptionContext>? callback, TimeSpan elapsedTime)
    {
        var ignore = Source.IsOptional;
        if (callback is not null)
        {
            var exceptionContext = new SecretsManagerExceptionContext(this, exception, elapsedTime);

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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var secretName = Source.SecretName;
        var secretVersion = Source.Version;
        var secretProcessor = Source.Processor;

        using var activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.LoadActivityName);
        try
        {
            var secret = await _secretFetcher.GetSecret(secretName, secretVersion, cancellationToken).ConfigureAwait(false);
            SetData(
                versionId: secret.VersionId,
                data: secretProcessor.GetConfigurationData(Source, secret.Value));

            activity?
                .AddEvent(new ActivityEvent("loaded"))
                .SetStatus(ActivityStatusCode.Ok, "Secret loaded");

            _logger.SecretLoaded(secretName, secret.VersionId);
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
            _logger.FailedToLoadSecret(ex, secretName);

            throw;
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        var secretName = Source.SecretName;
        var secretVersion = Source.Version;
        var secretProcessor = Source.Processor;

        using var activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.RefreshActivityName);
        try
        {
            var secret = await _secretFetcher.GetSecret(secretName, secretVersion, cancellationToken).ConfigureAwait(false);

            if (string.Equals(secret.VersionId, _currentSecretVersionId, StringComparison.Ordinal))
            {
                activity?
                    .AddEvent(new ActivityEvent("skipped"))
                    .SetStatus(ActivityStatusCode.Ok, "Secret up-to-date");

                _logger.SecretAlreadyLoaded(secretName, secret.VersionId);
                return;
            }

            var previousVersionId = _currentSecretVersionId;
            SetData(
                versionId: secret.VersionId,
                data: secretProcessor.GetConfigurationData(Source, secret.Value));

            activity?
                .AddEvent(new ActivityEvent("refreshed"))
                .SetStatus(ActivityStatusCode.Ok, "Secret refreshed");

            _logger.SecretRefreshed(secretName, previousVersionId!, secret.VersionId);
        }
        catch (Exception ex)
        {
#if NET9_0_OR_GREATER
            activity?
                .AddException(ex)
                .SetStatus(ActivityStatusCode.Error, "Error refreshing secret");
#else
            activity?
                .AddEvent(ex.ToActivityEvent())
                .SetStatus(ActivityStatusCode.Error, "Error refreshing secret");
#endif

            _logger.FailedToRefreshSecret(ex, secretName);
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);
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

    [LoggerMessage(555_2368_22, LogLevel.Information, "Secret {SecretName}:{VersionId} is already loaded, skipping refresh", EventName = "SecretAlreadyLoaded")]
    public static partial void SecretAlreadyLoaded(this ILogger logger, string secretName, string versionId);

    [LoggerMessage(555_2368_21, LogLevel.Information, "Secret {SecretName}:{PreviousVersionId}->{VersionId} has been refreshed", EventName = "SecretRefreshed")]
    public static partial void SecretRefreshed(this ILogger logger, string secretName, string previousVersionId, string versionId);

    [LoggerMessage(555_2368_20, LogLevel.Error, "Failed to refresh secret {SecretName}", EventName = "FailedToRefreshSecret")]
    public static partial void FailedToRefreshSecret(this ILogger logger, Exception exception, string secretName);
}