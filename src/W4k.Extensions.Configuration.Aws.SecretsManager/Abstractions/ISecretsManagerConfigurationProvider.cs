namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Secrets manager configuration provider.
/// </summary>
public interface ISecretsManagerConfigurationProvider
{
    /// <summary>
    /// Gets associated <see cref="SecretsManagerConfigurationSource"/>.
    /// </summary>
    SecretsManagerConfigurationSource Source { get; }

    /// <summary>
    /// Performs initial load of the configuration.
    /// </summary>
    void Load();

    /// <summary>
    /// Refreshes the configuration.
    /// </summary>
    void Refresh();
}