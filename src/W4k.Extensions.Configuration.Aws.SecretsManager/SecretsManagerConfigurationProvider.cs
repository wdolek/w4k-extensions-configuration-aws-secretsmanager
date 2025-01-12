using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretsManagerConfigurationProvider : ConfigurationProvider, IConfigurationRefresher
{
    private static readonly TimeSpan UnhandledExceptionDelay = TimeSpan.FromSeconds(5);

    private readonly SecretsManagerConfigurationSource _source;
    private readonly SecretFetcher _secretFetcher;
    private readonly ILogger _logger;

    private int _refreshInProgress;
    private string? _currentSecretVersionId;

    public SecretsManagerConfigurationProvider(SecretsManagerConfigurationSource source)
    {
        _source = source;
        _secretFetcher = new SecretFetcher(source.SecretsManager);
        _logger = source.Options.LoggerFactory.CreateLogger<SecretsManagerConfigurationProvider>();
    }

    public SecretsManagerConfigurationProviderOptions Options => _source.Options;
    public bool IsOptional => _source.Options.IsOptional;

    public override void Load()
    {
        var startingTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var cts = new CancellationTokenSource(Options.Startup.Timeout);
            LoadAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();

            // start watching for changes (if enabled)
            Options.ConfigurationWatcher?.Start(this);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch
        {
            if (Options.IsOptional)
            {
                return;
            }

            // delay re-throwing of exception to not overwhelm the system on startup code path
            // (this is to mitigate crash loop on startup)
            var elapsedTime = Stopwatch.GetElapsedTime(startingTimestamp);
            var waitBeforeRethrow = UnhandledExceptionDelay - elapsedTime;
            if (waitBeforeRethrow > TimeSpan.Zero)
            {
                Task.Delay(waitBeforeRethrow).ConfigureAwait(false).GetAwaiter().GetResult();
            }

            throw;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // return early if refresh is already in progress
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        var options = Options;
        var processor = options.Processor;
        try
        {
            var secret = await _secretFetcher.GetSecret(Options.SecretName, Options.Version, cancellationToken).ConfigureAwait(false);

            if (string.Equals(secret.VersionId, _currentSecretVersionId, StringComparison.Ordinal))
            {
                _logger.SecretAlreadyLoaded(options.SecretName, secret.VersionId);
                return;
            }

            var previousVersionId = _currentSecretVersionId;
            SetData(
                versionId: secret.VersionId,
                data: processor.GetConfigurationData(Options, secret.Value));

            _logger.SecretRefreshed(options.SecretName, previousVersionId!, secret.VersionId);
        }
        catch (Exception e)
        {
            _logger.FailedToRefreshSecret(e, options.SecretName);
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var options = Options;
        var processor = options.Processor;
        try
        {
            var secret = await _secretFetcher.GetSecret(options.SecretName, options.Version, cancellationToken).ConfigureAwait(false);
            SetData(
                versionId: secret.VersionId,
                data: processor.GetConfigurationData(options, secret.Value));

            _logger.SecretLoaded(options.SecretName, secret.VersionId);
        }
        catch (Exception e)
        {
            _logger.FailedToLoadSecret(e, options.SecretName);
            throw;
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