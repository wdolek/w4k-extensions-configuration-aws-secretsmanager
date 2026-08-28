using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Property("Category", "Integration")]
[NotInParallel]
public class FetchMultipleTests
{
    [Test]
    public async Task FetchMultipleSecrets()
    {
        // act
        var config = new ConfigurationBuilder()
            .AddSecretsManager(SecretsManagerTestFixture.SecretsManagerClient, SecretsManagerTestFixture.KeyValueSecretName)
            .AddSecretsManager(SecretsManagerTestFixture.SecretsManagerClient, SecretsManagerTestFixture.ComplexSecretName)
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // assert
        await Assert.That(secrets).Count().IsEqualTo(15);
    }

    [Test]
    public async Task FetchMultipleSecretsUsingSharedClient()
    {
        // act
        var config = new ConfigurationBuilder()
            .SetSecretsManagerClient(SecretsManagerTestFixture.SecretsManagerClient)
            .AddSecretsManager(SecretsManagerTestFixture.KeyValueSecretName)
            .AddSecretsManager(SecretsManagerTestFixture.ComplexSecretName)
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // assert
        await Assert.That(secrets).Count().IsEqualTo(15);
    }
}