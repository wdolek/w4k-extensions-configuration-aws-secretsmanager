using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Category("Integration")]
public class SecretsManagerConfigSourceShould
{
    [Test]
    public void ThrowWhenSecretNameNotSet()
    {
        // act & assert
        Assert.Throws<ArgumentException>(
            () =>
            {
                // using `AddSecretsManager(Action<SecretsManagerConfigurationSource>)` overload without setting `SecretName`
                new ConfigurationBuilder()
                    .AddSecretsManager(
                        source =>
                        {
                            source.SecretsManager = SecretsManagerTestFixture.SecretsManagerClient;
                        })
                    .Build();
            });
    }

    [Test]
    public void ThrowWhenSecretNotFound()
    {
        // act & assert
        Assert.Throws<SecretNotFoundException>(
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