using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

internal static class AmazonSecretsManagerExtensions
{
    public static Task CreateSecret(this IAmazonSecretsManager client, string secretName, string secretValue) =>
        client.CreateSecretAsync(
            new CreateSecretRequest
            {
                Name = secretName,
                SecretString = secretValue,
                Description = "W4k.Extensions.Configuration.Aws.SecretsManager integration tests secret",
            });

    public static Task UpdateSecret(this IAmazonSecretsManager client, string secretName, string secretValue) =>
        client.UpdateSecretAsync(new UpdateSecretRequest
        {
            SecretId = secretName,
            SecretString = secretValue,
        });

    public static Task DeleteSecret(this IAmazonSecretsManager client, string secretName) =>
        client.DeleteSecretAsync(
            new DeleteSecretRequest
            {
                SecretId = secretName,
                ForceDeleteWithoutRecovery = true,
            });
}