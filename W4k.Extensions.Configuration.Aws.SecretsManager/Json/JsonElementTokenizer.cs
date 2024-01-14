using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Json;

public interface IConfigurationTokenizer<in T>
{
    IEnumerable<KeyValuePair<string, string?>> Tokenize(T input, string prefix);
}

internal sealed class JsonElementTokenizer : IConfigurationTokenizer<JsonElement>
{
    [SuppressMessage("ReSharper", "CognitiveComplexity", Justification = "ಠ_ಠ")]
    public IEnumerable<KeyValuePair<string, string?>> Tokenize(JsonElement input, string prefix)
    {
        switch (input.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                yield return KeyValuePair.Create<string, string?>(prefix, null);
                break;

            case JsonValueKind.Number:
                yield return KeyValuePair.Create(prefix, input.GetRawText())!;
                break;

            case JsonValueKind.String:
                yield return KeyValuePair.Create(prefix, input.GetString());
                break;
                
            case JsonValueKind.True:
            case JsonValueKind.False:
                yield return KeyValuePair.Create(prefix, input.GetBoolean().ToString())!;
                break;

            case JsonValueKind.Object:
                foreach (var item in TokenizeObject(input, prefix))
                {
                    yield return item;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in TokenizeArray(input, prefix))
                {
                    yield return item;
                }
                break;

            // Should Not Happen™
            default:
                throw new FormatException("Unsupported JSON token");
        }
    }

    private IEnumerable<KeyValuePair<string, string?>> TokenizeObject(JsonElement element, string prefix)
    {
        foreach (var property in element.EnumerateObject())
        {
            var configKey = $"{prefix}{ConfigurationPath.KeyDelimiter}{property.Name}";
            foreach (var pair in Tokenize(property.Value, configKey))
            {
                yield return pair;
            }
        }
    }

    private IEnumerable<KeyValuePair<string, string?>> TokenizeArray(JsonElement element, string prefix)
    {
        var idx = 0;
        foreach (var arrayItem in element.EnumerateArray())
        {
            var configKey = $"{prefix}{ConfigurationPath.KeyDelimiter}{idx}";
            foreach (var pair in Tokenize(arrayItem, configKey))
            {
                yield return pair;
            }

            ++idx;
        }
    }
}