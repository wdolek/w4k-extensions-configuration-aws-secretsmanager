namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Configuration change watcher.
/// </summary>
public interface IConfigurationWatcher
{
    /// <summary>
    /// Start watching for configuration changes.
    /// </summary>
    /// <param name="provider">Configuration provider.</param>
    void Start(ISecretsManagerConfigurationProvider provider);

    /// <summary>
    /// Stops watching for configuration changes.
    /// </summary>
    void Stop();
}