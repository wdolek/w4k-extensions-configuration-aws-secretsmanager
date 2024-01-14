using System.Runtime.InteropServices;
using System.Text.Json;
using W4k.Extensions.Configuration.Aws.SecretsManager.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public interface ISecretsProcessor
{
    Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationProviderOptions options, string secretString);
}

public static class SecretsProcessor
{
    public static readonly ISecretsProcessor Json =
        new SecretsProcessor<JsonElement>(
            new JsonElementParser(), 
            new JsonElementTokenizer());
}

public class SecretsProcessor<T> : ISecretsProcessor
{
    private readonly ISecretStringParser<T> _parser;
    private readonly IConfigurationTokenizer<T> _tokenizer;

    public SecretsProcessor(ISecretStringParser<T> parser, IConfigurationTokenizer<T> tokenizer)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(tokenizer);
        _parser = parser;
        _tokenizer = tokenizer;
    }

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