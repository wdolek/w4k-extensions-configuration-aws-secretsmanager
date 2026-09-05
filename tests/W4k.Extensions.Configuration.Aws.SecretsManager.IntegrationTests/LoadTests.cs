using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Property("Category", "Integration")]
[NotInParallel]
public class LoadTests
{
    [Test]
    public async Task ThrowWhenSecretNotFound()
    {
        // act & assert
        await Assert.That(
                () =>
                {
                    new ConfigurationBuilder()
                        .AddSecretsManager(SecretsManagerTestFixture.SecretsManagerClient, "w4k/awssm/unknown-secret-mandatory")
                        .Build();
                })
            .ThrowsExactly<SecretRetrievalException>();
    }

    [Test]
    public async Task NotThrowWhenSecretIsOptional()
    {
        // act & assert
        IConfiguration config = null!;
        await Assert.That(
                () =>
                {
                    config = new ConfigurationBuilder()
                        .AddSecretsManager(
                            "w4k/awssm/unknown-secret-optional",
                            source => source
                                .WithSecretsManager(SecretsManagerTestFixture.SecretsManagerClient)
                                .OnLoadException(ctx => ctx.Ignore = true)
                                .OnReloadException(ctx => ctx.Ignore = true))
                        .Build();
                })
            .ThrowsNothing();

        await Assert.That(config).IsNotNull();
        await Assert.That(config.AsEnumerable()).IsEmpty();
    }
}