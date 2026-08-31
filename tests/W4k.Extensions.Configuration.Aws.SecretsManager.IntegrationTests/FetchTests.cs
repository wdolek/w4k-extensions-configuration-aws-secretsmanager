using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Property("Category", "Integration")]
[NotInParallel]
public class FetchTests
{
    [Test]
    public async Task FetchSecrets()
    {
        // arrange
        var expected = new KeyValuePair<string, string?>[]
        {
            new("ClientId", "my_client_id"),
            new("ClientSecret", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"),
        };

        // act
        var config = new ConfigurationBuilder()
            .AddSecretsManager(SecretsManagerTestFixture.SecretsManagerClient, SecretsManagerTestFixture.KeyValueSecretName)
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // act
        await Assert.That(secrets).Count().IsEqualTo(2);
        await Assert.That(secrets).IsEquivalentTo(expected);
    }

    [Test]
    public async Task FetchSecretsWithPrefix()
    {
        // arrange
        var expected = new KeyValuePair<string, string?>[]
        {
            new("App", null),
            new("App:Secrets", null),
            new("App:Secrets:ClientId", "my_client_id"),
            new("App:Secrets:ClientSecret", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"),
        };

        // act
        var config = new ConfigurationBuilder()
            .AddSecretsManager(
                SecretsManagerTestFixture.SecretsManagerClient,
                SecretsManagerTestFixture.KeyValueSecretName,
                configurationKeyPrefix: "App:Secrets")
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // assert
        await Assert.That(secrets).IsEquivalentTo(expected);
    }

    [Test]
    public async Task FetchSecretsWithKeyTransformation()
    {
        // arrange
        var expected = new KeyValuePair<string, string?>[]
        {
            new("id", "my_client_id"),
            new("secret", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"),
        };

        var customKeyTransformer = new TestKeyTransformer(s => s.Replace("Client", "").ToLowerInvariant());

        // act
        var config = new ConfigurationBuilder()
            .AddSecretsManager(
                SecretsManagerTestFixture.KeyValueSecretName,
                source => source
                    .WithSecretsManager(SecretsManagerTestFixture.SecretsManagerClient)
                    .AddKeyTransformer(customKeyTransformer))
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // act
        await Assert.That(secrets).IsEquivalentTo(expected);
    }

    [Test]
    public async Task FetchBinarySecret()
    {
        // arrange
        var expected = new KeyValuePair<string, string?>[]
        {
            new("SecretKey", TestSecrets.BinarySecretValue),
        };

        // act
        var config = new ConfigurationBuilder()
            .AddSecretsManager(
                SecretsManagerTestFixture.BinarySecretName,
                source => source
                    .WithSecretsManager(SecretsManagerTestFixture.SecretsManagerClient)
                    .WithProcessor(new PlainTextSecretProcessor()))
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // assert
        await Assert.That(secrets).IsEquivalentTo(expected);
    }

    [Test]
    public async Task FetchComplexJsonSecret()
    {
        // arrange
        var expected = new KeyValuePair<string, string?>[]
        {
            new("MyService", null),
            new("MyService:Username", "saanvis"),
            new("ApiKeys", null),
            new("ApiKeys:Citizenship", "rosebud"),
            new("ApiKeys:Universe", "42"),
            new("PIN", null),
            new("PIN:0", "5"),
            new("PIN:1", "5"),
            new("PIN:2", "5"),
            new("PIN:3", "2"),
            new("PIN:4", "3"),
            new("PIN:5", "6"),
            new("PIN:6", "8"),
        };

        // act
        var config = new ConfigurationBuilder()
            .AddSecretsManager(SecretsManagerTestFixture.SecretsManagerClient, SecretsManagerTestFixture.ComplexSecretName)
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // assert
        await Assert.That(secrets).IsEquivalentTo(expected);
    }

    private sealed class PlainTextSecretProcessor : ISecretProcessor
    {
        public Dictionary<string, string?> GetConfigurationData(SecretsManagerConfigurationSource source, string secretString) =>
            new() { ["SecretKey"] = secretString };
    }

    private class TestKeyTransformer : IConfigurationKeyTransformer
    {
        private readonly Func<string, string> _transform;

        public TestKeyTransformer(Func<string, string> transform)
        {
            _transform = transform;
        }

        public string Transform(string key) => _transform(key);
    }
}