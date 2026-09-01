using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Processor of plain text secrets, mapping the whole secret string to a single configuration value.
/// </summary>
/// <remarks>
/// <para>
/// When created without arguments, the value is placed under
/// <see cref="SecretsManagerConfigurationSource.ConfigurationKeyPrefix"/> verbatim.
/// </para>
/// <para>
/// When created with an explicit configuration key, the value is placed under
/// <c>{ConfigurationKeyPrefix}:{configurationKey}</c> (composed the same way as object keys
/// of the JSON processor), or under the key standalone when no prefix is set.
/// </para>
/// <para>
/// The secret string is used as-is, trailing newlines are preserved. Key transformers
/// are applied to the resulting configuration key.
/// </para>
/// </remarks>
public sealed class PlainTextSecretProcessor : ISecretProcessor
{
    private readonly string? _configurationKey;

    /// <summary>
    /// Initializes new instance of <see cref="PlainTextSecretProcessor"/>.
    /// </summary>
    /// <remarks>
    /// The secret value is placed under <see cref="SecretsManagerConfigurationSource.ConfigurationKeyPrefix"/>
    /// verbatim. An empty prefix is a configuration error - a value cannot live at the configuration root -
    /// and results in <see cref="InvalidOperationException"/> thrown at processing time.
    /// </remarks>
    public PlainTextSecretProcessor()
    {
    }

    /// <summary>
    /// Initializes new instance of <see cref="PlainTextSecretProcessor"/> with explicit configuration key.
    /// </summary>
    /// <param name="configurationKey">Configuration key the secret value is placed under.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configurationKey"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    public PlainTextSecretProcessor(string configurationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);
        _configurationKey = configurationKey;
    }

    /// <inheritdoc/>
    public Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationSource source, string secretString)
    {
        var key = _configurationKey is null
            ? source.ConfigurationKeyPrefix
            : ComposeKey(source.ConfigurationKeyPrefix, _configurationKey);

        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException(
                "Configuration key is not set, a value cannot live at the configuration root. "
                + "Set configuration key prefix or use 'PlainTextSecretProcessor(string configurationKey)' constructor.");
        }

        // key transformers are snapshotted when the source is built, so mutating
        // the source list afterwards does not affect (reload) processing
        var transformers = source.KeyTransformersSnapshot is { } snapshot
            ? snapshot.AsSpan()
            : CollectionsMarshal.AsSpan(source.KeyTransformers);

        var transformedKey = key;
        foreach (var transformer in transformers)
        {
            transformedKey = transformer.Transform(transformedKey);
        }

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [transformedKey] = secretString,
        };
    }

    private static string ComposeKey(string prefix, string key) =>
        string.IsNullOrEmpty(prefix)
            ? key
            : $"{prefix}{ConfigurationPath.KeyDelimiter}{key}";
}