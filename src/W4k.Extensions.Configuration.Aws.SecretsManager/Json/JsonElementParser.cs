using System.Text.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Json;

internal sealed class JsonElementParser : ISecretStringParser<JsonElement>
{
    private static readonly JsonDocumentOptions DefaultJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16,
    };

    public bool TryParse(string secretString, out JsonElement secretValue)
    {
        if (!IsPossiblyJsonValue(secretString))
        {
            secretValue = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(secretString, DefaultJsonDocumentOptions);
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

        var startChar = secretSpan[0];
        var endChar = secretSpan[^1];

        return (startChar == '{' && endChar == '}') || (startChar == '[' && endChar == ']');
    }
}