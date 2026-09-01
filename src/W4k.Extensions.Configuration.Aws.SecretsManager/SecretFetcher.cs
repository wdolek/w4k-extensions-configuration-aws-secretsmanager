using System.Text;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretFetcher
{
    // strict UTF-8, non-UTF-8 payloads fail loudly instead of being silently
    // corrupted with U+FFFD replacement characters (see C7)
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
            return new(response.ARN, response.VersionId, response.SecretString);
        }

        if (response.SecretBinary is not null)
        {
            // the AWS SDK has already base64-decoded the payload into the stream, read it as-is
            using var binary = response.SecretBinary;
            var secretString = Utf8Strict.GetString(binary.GetBuffer(), 0, (int)binary.Length);

            return new(response.ARN, response.VersionId, secretString);
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
    public SecretValue(string? arn, string versionId, string value)
    {
        Arn = arn;
        VersionId = versionId;
        Value = value;
    }

    public string? Arn { get; }
    public string VersionId { get; }
    public string Value { get; }
}