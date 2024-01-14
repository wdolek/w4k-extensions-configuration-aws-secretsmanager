using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretsFetcher(IAmazonSecretsManager secretsManager)
{
    private readonly IAmazonSecretsManager _secretsManager = secretsManager;

    public async Task<string> GetSecretString(string secretId, SecretVersionBase? version)
    {
        var request = CreateRequest(secretId, version);
        try
        {
            var response = await _secretsManager.GetSecretValueAsync(request).ConfigureAwait(false);

            if (response.SecretString is not null)
            {
                return response.SecretString;
            }

            if (response.SecretBinary is not null)
            {
                using var reader = new StreamReader(response.SecretBinary, leaveOpen: false);
                var encodedString = await reader.ReadToEndAsync().ConfigureAwait(false);

                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedString));
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

    private static GetSecretValueRequest CreateRequest(string secretId, SecretVersionBase? version)
    {
        var request = new GetSecretValueRequest
        {
            SecretId = secretId,
        };

        switch (version)
        {
            case SecretVersion secretVersion:
                request.VersionId = secretVersion.Id;
                break;
            case StagedSecretVersion stagedSecretVersion:
                request.VersionStage = stagedSecretVersion.Stage;
                break;
        }

        return request;
    }
}