using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Category("Integration")]
public class ConfigurationValidationTests
{
    [Test]
    public void ThrowWhenSecretNameNotSet()
    {
        // act & assert
        Assert.Throws<InvalidOperationException>(
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
            });
    }
}