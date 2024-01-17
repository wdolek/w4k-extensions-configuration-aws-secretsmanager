namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Configuration change watcher.
/// </summary>
public interface IConfigurationWatcher
{
    /// <summary>
    /// Start watching for configuration changes.
    /// </summary>
    /// <param name="refresher">Configuration refresher.</param>
    void Start(IConfigurationRefresher refresher);
}