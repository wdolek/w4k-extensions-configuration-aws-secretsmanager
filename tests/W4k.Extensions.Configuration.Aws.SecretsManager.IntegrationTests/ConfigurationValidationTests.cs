using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Property("Category", "Integration")]
[NotInParallel]
public class ConfigurationValidationTests
{
    [Test]
    public async Task ThrowWhenSecretNameNotSet()
    {
        // act & assert
        await Assert.That(
                () =>
                {
                    // using `AddSecretsManager` overload without setting `SecretName`
                    new ConfigurationBuilder()
                        .AddSecretsManager(
                            source =>
                            {
                                source.SecretsManager = SecretsManagerTestFixture.SecretsManagerClient;
                            })
                        .Build();
                })
            .ThrowsExactly<InvalidOperationException>();
    }
}