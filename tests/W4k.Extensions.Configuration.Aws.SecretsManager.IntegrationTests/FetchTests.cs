using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Category("Integration")]
public class FetchTests
{
    [Test]
    public void FetchSecrets()
    {
        // arrange
        var expected = new KeyValuePair<string, string>[]
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
        Assert.That(secrets, Has.Count.EqualTo(2));
        Assert.That(secrets, Is.EquivalentTo(expected));
    }

    [Test]
    public void FetchSecretsWithPrefix()
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
        Assert.That(secrets, Is.EquivalentTo(expected));
    }

    [Test]
    public void FetchSecretsWithKeyTransformation()
    {
        // arrange
        var expected = new KeyValuePair<string, string>[]
        {
            new("id", "my_client_id"),
            new("secret", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"),
        };

        var customKeyTransformer = new TestKeyTransformer(s => s.Replace("Client", "").ToLowerInvariant());

        // act
        var config = new ConfigurationBuilder()
            .AddSecretsManager(
                source =>
                {
                    source.SecretsManager = SecretsManagerTestFixture.SecretsManagerClient;
                    source.SecretName = SecretsManagerTestFixture.KeyValueSecretName;
                    source.KeyTransformers.Add(customKeyTransformer);
                })
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // act
        Assert.That(secrets, Is.EquivalentTo(expected));
    }

    [Test]
    public void FetchComplexJsonSecret()
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
        Assert.That(secrets, Is.EquivalentTo(expected));
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