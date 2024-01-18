namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Refreshable configuration provider for AWS Secrets Manager.
/// </summary>
public interface IConfigurationRefresher
{
    /// <summary>
    /// Gets configuration provider options.
    /// </summary>
    SecretsManagerConfigurationProviderOptions Options { get; }

    /// <summary>
    /// Gets whether configuration source is optional.
    /// </summary>
    bool IsOptional { get; }

    /// <summary>
    /// Invoke refresh of configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Task representing refresh operation.
    /// </returns>
    Task RefreshAsync(CancellationToken cancellationToken);
}