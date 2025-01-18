using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecretsManager;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[SetUpFixture]
public class SecretsManagerTestFixture
{
    private const string AwsProfileName = "w4ktest@admin";

    public static IAmazonSecretsManager SecretsManagerClient { get; private set; } = null!;

    public static string KeyValueSecretName { get; private set; } = "";
    public static string ComplexSecretName { get; private set; } = "";

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        var storeChain = new CredentialProfileStoreChain();
        if (!storeChain.TryGetAWSCredentials(AwsProfileName, out var credentials))
        {
            throw new InvalidOperationException($"""Unable to get AWS credentials using "{AwsProfileName}" profile.""");
        }

        var guid = Guid.NewGuid().ToString("N")[^8..];
        var client = new AmazonSecretsManagerClient(credentials, RegionEndpoint.EUWest1);

        KeyValueSecretName = $"{TestSecrets.KeyValueSecretName}/{guid}";
        client.CreateSecret(KeyValueSecretName, TestSecrets.KeyValueJson).GetAwaiter().GetResult();

        ComplexSecretName = $"{TestSecrets.ComplexSecretName}/{guid}";
        client.CreateSecret(ComplexSecretName, TestSecrets.ComplexJson).GetAwaiter().GetResult();

        SecretsManagerClient = client;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        var client = SecretsManagerClient;

        client.DeleteSecret(KeyValueSecretName).GetAwaiter().GetResult();
        client.DeleteSecret(ComplexSecretName).GetAwaiter().GetResult();

        client.Dispose();
        SecretsManagerClient = null!;
    }
}