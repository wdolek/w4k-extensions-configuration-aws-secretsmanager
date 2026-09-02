using System.Text;
using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecretsManager;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

public static class SecretsManagerTestFixture
{
    private const string AwsProfileName = "w4ktest@admin";

    public static IAmazonSecretsManager SecretsManagerClient { get; private set; } = null!;

    public static string KeyValueSecretName { get; private set; } = "";
    public static string ComplexSecretName { get; private set; } = "";
    public static string BinarySecretName { get; private set; } = "";
    public static string InvalidUtf8BinarySecretName { get; private set; } = "";
    public static string PlainTextSecretName { get; private set; } = "";

    [Before(Assembly)]
    public static void OneTimeSetup()
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

        BinarySecretName = $"{TestSecrets.BinarySecretName}/{guid}";
        client.CreateBinarySecret(BinarySecretName, Encoding.UTF8.GetBytes(TestSecrets.BinarySecretValue)).GetAwaiter().GetResult();

        InvalidUtf8BinarySecretName = $"{TestSecrets.InvalidUtf8BinarySecretName}/{guid}";
        client.CreateBinarySecret(InvalidUtf8BinarySecretName, TestSecrets.InvalidUtf8BinarySecretValue).GetAwaiter().GetResult();

        PlainTextSecretName = $"{TestSecrets.PlainTextSecretName}/{guid}";
        client.CreateSecret(PlainTextSecretName, TestSecrets.PlainTextSecretValue).GetAwaiter().GetResult();

        SecretsManagerClient = client;
    }

    [After(Assembly)]
    public static void OneTimeTearDown()
    {
        var client = SecretsManagerClient;

        client.DeleteSecret(KeyValueSecretName).GetAwaiter().GetResult();
        client.DeleteSecret(ComplexSecretName).GetAwaiter().GetResult();
        client.DeleteSecret(BinarySecretName).GetAwaiter().GetResult();
        client.DeleteSecret(InvalidUtf8BinarySecretName).GetAwaiter().GetResult();
        client.DeleteSecret(PlainTextSecretName).GetAwaiter().GetResult();

        client.Dispose();
        SecretsManagerClient = null!;
    }
}