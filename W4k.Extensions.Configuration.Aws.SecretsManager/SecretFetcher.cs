using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretFetcher
{
    private readonly IAmazonSecretsManager _secretsManager;

    public SecretFetcher(IAmazonSecretsManager secretsManager)
    {
        _secretsManager = secretsManager;
    }

    public async Task<SecretValue> GetSecret(string secretId, SecretVersion? version, CancellationToken cancellationToken)
    {
        var request = CreateRequest(secretId, version);
        try
        {
            var response = await _secretsManager.GetSecretValueAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.SecretString is not null)
            {
                return new(response.VersionId, response.SecretString);
            }

            if (response.SecretBinary is not null)
            {
                using var reader = new StreamReader(response.SecretBinary, leaveOpen: false);
#if NET8_0_OR_GREATER
                var encodedString = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#else
                var encodedString = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
                var secretString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedString));

                return new(response.VersionId, secretString);
            }

            throw new SecretRetrievalException($"Secret {request.SecretId} is neither string nor binary");
        }
        catch (ResourceNotFoundException e)
        {
            throw new SecretNotFoundException($"Secret {request.SecretId} not found", e);
        }
        catch (Exception e)
        {
            throw new SecretRetrievalException($"Failed to retrieve secret {request.SecretId} from AWS Secrets Manager", e);
        }
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
        Value = value;
        VersionId = versionId;
    }

    public string Value { get; }
    public string VersionId { get; }
}