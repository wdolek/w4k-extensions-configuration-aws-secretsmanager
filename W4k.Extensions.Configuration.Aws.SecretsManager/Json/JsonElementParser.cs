using System.Text.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Json;

public interface ISecretStringParser<T>
{
    bool TryParse(string secret, out T jsonElement);
}

internal class JsonElementParser : ISecretStringParser<JsonElement>
{
    private static readonly JsonDocumentOptions DefaultJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16,
    };
    
    public bool TryParse(string secret, out JsonElement jsonElement)
    {
        if (!IsPossiblyJsonValue(secret))
        {
            jsonElement = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(secret, DefaultJsonDocumentOptions);
            jsonElement = document.RootElement.Clone();

            return true;
        }
        catch (JsonException)
        {
            jsonElement = default;
            return false;
        }
    }

    private static bool IsPossiblyJsonValue(string secret)
    {
        var secretSpan = secret.AsSpan().Trim();
        if (secretSpan.Length < 2)
        {
            return false;
        }

        return (secretSpan[0] == '{' && secretSpan[^1] == '}') || (secretSpan[0] == '[' && secretSpan[^1] == ']');
    }
}