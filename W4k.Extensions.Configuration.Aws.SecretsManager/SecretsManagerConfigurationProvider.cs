using System.Diagnostics;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretsManagerConfigurationProvider : ConfigurationProvider, IConfigurationRefresher
{
    private static readonly TimeSpan UnhandledExceptionDelay = TimeSpan.FromSeconds(5);
    
    private readonly SecretsFetcher _secretsFetcher;
    private readonly SecretsManagerConfigurationProviderOptions _options;
    private readonly bool _isOptional;

    private int _refreshInProgress;
    private string? _currentSecretVersionId;

    public SecretsManagerConfigurationProvider(
        IAmazonSecretsManager secretsManager,
        SecretsManagerConfigurationProviderOptions options,
        bool isOptional)
    {
        _secretsFetcher = new SecretsFetcher(secretsManager);
        _options = options;
        _isOptional = isOptional;
    }
    
    public string Name => _options.SecretName;

    public override void Load()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var cts = new CancellationTokenSource(_options.Startup.Timeout);
            LoadAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();

            // start watching for changes; if initial load fails, watcher is not started
            _options.ConfigurationWatcher?.Start(this);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch
        {
            if (_isOptional)
            {
                return;
            }
            
            // delay re-throwing of exception to not overwhelm the system on startup code path
            // (this is to mitigate crash loop on startup)
            var waitBeforeRethrow = UnhandledExceptionDelay - watch.Elapsed;
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

        try
        {
            var secret = await _secretsFetcher.GetSecret(_options.SecretName, _options.Version, cancellationToken).ConfigureAwait(false);
            if (string.Equals(secret.VersionId, _currentSecretVersionId, StringComparison.Ordinal))
            {
                return;
            }

            SetData(
                versionId: secret.VersionId, 
                data: _options.Processor.GetConfigurationData(_options, secret.Value));
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var secret = await _secretsFetcher.GetSecret(_options.SecretName, _options.Version, cancellationToken).ConfigureAwait(false);
        SetData(
            versionId: secret.VersionId, 
            data: _options.Processor.GetConfigurationData(_options, secret.Value));
    }

    private void SetData(string versionId, Dictionary<string, string?> data)
    {
        _currentSecretVersionId = versionId;
        Data = data;

        OnReload();
    }
}