namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Refreshable configuration provider for AWS Secrets Manager.
/// </summary>
public interface IConfigurationRefresher
{
    /// <summary>
    /// Gets name of configuration provider, typically it's secret name.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Invoke refresh of configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Task representing refresh operation.
    /// </returns>
    Task RefreshAsync(CancellationToken cancellationToken);
}