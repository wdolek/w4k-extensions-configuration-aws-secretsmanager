﻿using System.Runtime.InteropServices;
using System.Text.Json;
using W4k.Extensions.Configuration.Aws.SecretsManager.Json;

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

/// <summary>
/// Default secrets processor reference container.
/// </summary>
public static class SecretsProcessor
{
    /// <summary>
    /// Processor of JSON secrets.
    /// </summary>
    public static readonly ISecretsProcessor Json =
        new SecretsProcessor<JsonElement>(
            new JsonElementParser(), 
            new JsonElementTokenizer());
}

/// <inheritdoc/>
/// <remarks>
/// This is helper class to simplify creation of custom secrets processor, but it's not required to use it.
/// </remarks>
public class SecretsProcessor<T> : ISecretsProcessor
{
    private readonly ISecretStringParser<T> _parser;
    private readonly IConfigurationTokenizer<T> _tokenizer;

    /// <summary>
    /// Initializes new instance of <see cref="SecretsProcessor{T}"/>.
    /// </summary>
    /// <param name="parser">Secret string parser.</param>
    /// <param name="tokenizer">Configuration tokenizer.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="parser"/> or <paramref name="tokenizer"/> is <c>null</c>.
    /// </exception>
    public SecretsProcessor(ISecretStringParser<T> parser, IConfigurationTokenizer<T> tokenizer)
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
            throw new FormatException($"Secret {options.SecretId} cannot be parsed from JSON, object or array expected");
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