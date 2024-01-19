using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretsManagerConfigurationProvider : ConfigurationProvider, IConfigurationRefresher
{
    private static readonly TimeSpan UnhandledExceptionDelay = TimeSpan.FromSeconds(5);

    private readonly SecretFetcher _secretFetcher;

    private int _refreshInProgress;
    private string? _currentSecretVersionId;

    public SecretsManagerConfigurationProvider(SecretsManagerConfigurationSource source, bool isOptional)
    {
        Options = source.Options;
        IsOptional = isOptional;

        _secretFetcher = new SecretFetcher(source.SecretsManager);
    }

    public SecretsManagerConfigurationProviderOptions Options { get; }
    public bool IsOptional { get; }

    public override void Load()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var cts = new CancellationTokenSource(Options.Startup.Timeout);
            LoadAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();

            // start watching for changes (if enabled)
            // NB! if load fails (exception is thrown) then watcher is not started
            Options.ConfigurationWatcher?.Start(this);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch
        {
            if (IsOptional)
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
        using var activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.RefreshActivityName);

        // return early if refresh is already in progress
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            var secret = await _secretFetcher.GetSecret(Options.SecretName, Options.Version, cancellationToken).ConfigureAwait(false);
            if (string.Equals(secret.VersionId, _currentSecretVersionId, StringComparison.Ordinal))
            {
                activity?
                    .AddEvent(new ActivityEvent("Skip, no change"))
                    .SetStatus(ActivityStatusCode.Ok);

                return;
            }

            SetData(
                versionId: secret.VersionId, 
                data: Options.Processor.GetConfigurationData(Options, secret.Value));
            
            activity?
                .AddEvent(new ActivityEvent("Refresh completed"))
                .SetStatus(ActivityStatusCode.Ok);
        }
        catch(Exception e)
        {
            activity?
                .AddEvent(new ActivityEvent("Refresh failed"))
                .SetTag("Error", e.Message)
                .SetStatus(ActivityStatusCode.Error);

            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivityDescriptors.Source.StartActivity(ActivityDescriptors.LoadActivityName);

        try
        {
            var secret = await _secretFetcher.GetSecret(Options.SecretName, Options.Version, cancellationToken).ConfigureAwait(false);
            SetData(
                versionId: secret.VersionId, 
                data: Options.Processor.GetConfigurationData(Options, secret.Value));

            activity?
                .AddEvent(new ActivityEvent("Load completed"))
                .SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception e)
        {
            activity?
                .AddEvent(new ActivityEvent("Load failed"))
                .SetTag("Error", e.Message)
                .SetStatus(ActivityStatusCode.Error);

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