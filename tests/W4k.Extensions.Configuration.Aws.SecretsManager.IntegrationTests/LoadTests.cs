using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Category("Integration")]
public class LoadTests
{
    [Test]
    public void ThrowWhenSecretNotFound()
    {
        // act & assert
        Assert.Throws<SecretRetrievalException>(
            () =>
            {
                new ConfigurationBuilder()
                    .AddSecretsManager(SecretsManagerTestFixture.SecretsManagerClient, "w4k/awssm/unknown-secret-mandatory")
                    .Build();
            });
    }

    [Test]
    public void NotThrowWhenSecretIsOptional()
    {
        // act & assert
        IConfiguration config = null!;
        Assert.DoesNotThrow(
            () =>
            {
                config = new ConfigurationBuilder()
                    .AddSecretsManager(
                        SecretsManagerTestFixture.SecretsManagerClient,
                        "w4k/awssm/unknown-secret-optional",
                        isOptional: true)
                    .Build();
            });

        Assert.That(config, Is.Not.Null);
        Assert.That(config.AsEnumerable(), Is.Empty);
    }
}