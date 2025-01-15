namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Secret string processor, responsible for parsing and tokenizing secret string.
/// </summary>
public interface ISecretProcessor
{
    /// <summary>
    /// Processes secret string and returns configuration data.
    /// </summary>
    /// <param name="source">Secrets manager provider source.</param>
    /// <param name="secretString">Content of secrets in string form.</param>
    /// <returns>Dictionary of key-value configuration read from <paramref name="secretString"/>.</returns>
    Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationSource source, string secretString);
}