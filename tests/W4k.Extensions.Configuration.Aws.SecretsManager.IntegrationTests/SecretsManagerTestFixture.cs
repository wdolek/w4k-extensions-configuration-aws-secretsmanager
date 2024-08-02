using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[SetUpFixture]
public class SecretsManagerTestFixture
{
    private const string AwsProfileName = "w4ktest@admin";

    public static IAmazonSecretsManager SecretsManagerClient { get; private set; } = null!;

    public static string KeyValueSecretName { get; private set; } = "";
    public static string ComplexSecretName { get; private set; } = "";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var storeChain = new CredentialProfileStoreChain();
        if (!storeChain.TryGetAWSCredentials(AwsProfileName, out var credentials))
        {
            throw new InvalidOperationException($"""Unable to get AWS credentials using "{AwsProfileName}" profile.""");
        }

        var guid = Guid.NewGuid().ToString("N")[^8..];
        KeyValueSecretName = $"{TestSecrets.KeyValueSecretName}/{guid}";
        ComplexSecretName = $"{TestSecrets.ComplexSecretName}/{guid}";

        var client = new AmazonSecretsManagerClient(credentials, RegionEndpoint.EUWest1);
        CreateSecret(client, KeyValueSecretName, TestSecrets.KeyValueJson).GetAwaiter().GetResult();
        CreateSecret(client, ComplexSecretName, TestSecrets.ComplexJson).GetAwaiter().GetResult();

        SecretsManagerClient = client;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        var client = SecretsManagerClient;
        DeleteSecret(client, KeyValueSecretName).GetAwaiter().GetResult();
        DeleteSecret(client, ComplexSecretName).GetAwaiter().GetResult();

        client.Dispose();
        SecretsManagerClient = null!;
    }

    private static Task CreateSecret(IAmazonSecretsManager client, string secretName, string secretValue) =>
        client.CreateSecretAsync(
            new CreateSecretRequest
            {
                Name = secretName,
                SecretString = secretValue,
                Description = "W4k.Extensions.Configuration.Aws.SecretsManager integration secret"
            });

    private static Task DeleteSecret(IAmazonSecretsManager client, string secretName) =>
        client.DeleteSecretAsync(
            new DeleteSecretRequest
            {
                SecretId = secretName,
                ForceDeleteWithoutRecovery = true,
            });
}