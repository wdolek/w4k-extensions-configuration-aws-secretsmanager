using System.Text.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Json;

internal sealed class JsonElementParser : ISecretsStringParser<JsonElement>
{
    private static readonly JsonDocumentOptions DefaultJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16,
    };
    
    public bool TryParse(string secret, out JsonElement secretValue)
    {
        if (!IsPossiblyJsonValue(secret))
        {
            secretValue = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(secret, DefaultJsonDocumentOptions);
            secretValue = document.RootElement.Clone();

            return true;
        }
        catch (JsonException)
        {
            secretValue = default;
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