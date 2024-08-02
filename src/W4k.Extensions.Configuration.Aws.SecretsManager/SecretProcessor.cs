using System.Runtime.InteropServices;
using System.Text.Json;
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
}

/// <inheritdoc/>
/// <remarks>
/// Helper class to simplify creation of custom secrets processor.
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
    public Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationProviderOptions options, string secretString)
    {
        if (!_parser.TryParse(secretString, out var secretValue))
        {
            throw new FormatException($"Secret '{options.SecretName}' cannot be parsed, have you used appropriate secrets processor?");
        }

        var transformers = CollectionsMarshal.AsSpan(options.KeyTransformers);

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in _tokenizer.Tokenize(secretValue, options.ConfigurationKeyPrefix))
        {
            var transformedKey = key;
            foreach (var t in transformers)
            {
                transformedKey = t.Transform(transformedKey);
            }

            data[transformedKey] = value;
        }

        return data;
    }
}