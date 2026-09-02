using System.Text.Json;
using Microsoft.Extensions.Configuration;
using W4k.Extensions.Configuration.Aws.SecretsManager.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Default secrets processor reference container.
/// </summary>
public static class SecretsProcessor
{
    /// <summary>
    /// Processor of JSON secrets.
    /// </summary>
    public static readonly ISecretProcessor Json =
        new SecretProcessor<JsonElement>(
            new JsonElementParser(),
            new JsonElementTokenizer());

    /// <summary>
    /// Processor of plain text secrets, places the whole secret string under configuration key prefix.
    /// </summary>
    /// <remarks>
    /// Use <see cref="PlainTextSecretProcessor(string)"/> constructor to place the value
    /// under an explicit configuration key.
    /// </remarks>
    public static readonly ISecretProcessor PlainText = new PlainTextSecretProcessor();
}

/// <inheritdoc/>
/// <remarks>
/// Helper class to simplify creation of custom secrets' processor.
/// </remarks>
public class SecretProcessor<T> : ISecretProcessor
{
    private readonly ISecretStringParser<T> _parser;
    private readonly IConfigurationTokenizer<T> _tokenizer;

    /// <summary>
    /// Initializes new instance of <see cref="SecretProcessor{T}"/>.
    /// </summary>
    /// <param name="parser">Secret string parser.</param>
    /// <param name="tokenizer">Configuration tokenizer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="parser"/> or <paramref name="tokenizer"/> is <see langword="null"/>.</exception>
    public SecretProcessor(ISecretStringParser<T> parser, IConfigurationTokenizer<T> tokenizer)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(tokenizer);
        _parser = parser;
        _tokenizer = tokenizer;
    }

    /// <inheritdoc/>
    public Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationSource source, string secretString)
    {
        if (!_parser.TryParse(secretString, out var secretValue))
        {
            throw new FormatException(
                $"Secret '{source.SecretName}' cannot be parsed, have you used appropriate secrets processor?");
        }

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in _tokenizer.Tokenize(secretValue, source.ConfigurationKeyPrefix))
        {
            var transformedKey = key;
            foreach (var transformer in source.GetKeyTransformers())
            {
                transformedKey = transformer.Transform(transformedKey);
            }

            data[transformedKey] = value;
        }

        return data;
    }
}

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
                "Configuration key is not set, a value cannot live at the configuration root. Set configuration key prefix or use 'PlainTextSecretProcessor(string configurationKey)' constructor.");
        }

        var transformedKey = key;
        foreach (var transformer in source.GetKeyTransformers())
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