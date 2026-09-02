using System.Text;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretFetcher
{
    // strict UTF-8: non-UTF-8 payloads fail loudly instead of being silently
    // corrupted with U+FFFD replacement characters (see ADR-0014)
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IAmazonSecretsManager _secretsManager;

    public SecretFetcher(IAmazonSecretsManager secretsManager)
    {
        _secretsManager = secretsManager;
    }

    public async Task<SecretValue> GetSecret(string secretId, SecretVersion? version, CancellationToken cancellationToken)
    {
        var request = CreateRequest(secretId, version);

        var response = await _secretsManager.GetSecretValueAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.SecretString is not null)
        {
            return new(response.VersionId, response.SecretString);
        }

        if (response.SecretBinary is not null)
        {
            // the AWS SDK has already base64-decoded the payload into the stream, read it as-is
            using var binary = response.SecretBinary;

            try
            {
                var secretString = Utf8Strict.GetString(binary.GetBuffer(), 0, (int)binary.Length);
                return new(response.VersionId, secretString);
            }
            catch (DecoderFallbackException)
            {
                // do not chain the original DecoderFallbackException as InnerException:
                // its default Message embeds the offending raw bytes
                throw new SecretRetrievalException(
                    $"Secret '{request.SecretId}' is stored as binary and its content is not valid UTF-8; binary secrets must decode as UTF-8 text (see ADR-0014)",
                    request.SecretId);
            }
        }

        // Should Not Happen™
        throw new SecretRetrievalException($"Secret {request.SecretId} is neither string nor binary", request.SecretId);
    }

    private static GetSecretValueRequest CreateRequest(string secretId, SecretVersion? version)
    {
        var request = new GetSecretValueRequest
        {
            SecretId = secretId,
        };

        if (version is not null)
        {
            if (!string.IsNullOrEmpty(version.VersionId))
            {
                request.VersionId = version.VersionId;
            }

            if (!string.IsNullOrEmpty(version.VersionStage))
            {
                request.VersionStage = version.VersionStage;
            }
        }

        return request;
    }
}

internal sealed class SecretValue
{
    public SecretValue(string versionId, string value)
    {
        VersionId = versionId;
        Value = value;
    }

    public string VersionId { get; }
    public string Value { get; }
}