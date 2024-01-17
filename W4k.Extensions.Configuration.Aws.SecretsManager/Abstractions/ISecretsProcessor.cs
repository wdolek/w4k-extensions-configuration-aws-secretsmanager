namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Secrets processor component, responsible for parsing and tokenizing secret string.
/// </summary>
public interface ISecretsProcessor
{
    /// <summary>
    /// Processes secret string and returns configuration data.
    /// </summary>
    /// <param name="options">Secrets manager provider options.</param>
    /// <param name="secretString">Content of secrets in string form.</param>
    /// <returns>Dictionary of key-value configuration read from <paramref name="secretString"/>.</returns>
    Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationProviderOptions options, string secretString);
}