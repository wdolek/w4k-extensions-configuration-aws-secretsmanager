using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Configuration key transformer to modify tokenized configuration keys.
/// </summary>
public interface IConfigurationKeyTransformer
{
    /// <summary>
    /// Transforms tokenized configuration key to new value.
    /// </summary>
    /// <param name="key">Configuration key.</param>
    /// <returns>Modified configuration key.</returns>
    string Transform(string key);
}

/// <summary>
/// Key-delimiter transformer, replacing <c>__</c> with <see cref="ConfigurationPath.KeyDelimiter"/>.
/// </summary>
public sealed class KeyDelimiterTransformer : IConfigurationKeyTransformer
{
    /// <inheritdoc/>
    public string Transform(string key) => key.Replace("__", ConfigurationPath.KeyDelimiter);
}