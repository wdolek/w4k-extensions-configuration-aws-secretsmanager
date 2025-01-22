using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Category("Integration")]
public class FetchMultipleTests
{
    [Test]
    public void FetchMultipleSecrets()
    {
        // act
        var config = new ConfigurationBuilder()
            .AddSecretsManager(SecretsManagerTestFixture.SecretsManagerClient, SecretsManagerTestFixture.KeyValueSecretName)
            .AddSecretsManager(SecretsManagerTestFixture.SecretsManagerClient, SecretsManagerTestFixture.ComplexSecretName)
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // assert
        Assert.That(secrets, Has.Count.EqualTo(15));
    }

    [Test]
    public void FetchMultipleSecretsUsingSharedClient()
    {
        // act
        var config = new ConfigurationBuilder()
            .SetSecretsManagerClient(SecretsManagerTestFixture.SecretsManagerClient)
            .AddSecretsManager(SecretsManagerTestFixture.KeyValueSecretName)
            .AddSecretsManager(SecretsManagerTestFixture.ComplexSecretName)
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // assert
        Assert.That(secrets, Has.Count.EqualTo(15));
    }
}