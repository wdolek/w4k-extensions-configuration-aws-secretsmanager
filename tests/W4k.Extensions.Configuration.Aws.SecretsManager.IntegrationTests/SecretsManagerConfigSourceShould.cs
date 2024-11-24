using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

[Category("Integration")]
public class SecretsManagerConfigSourceShould
{
    [Test]
    public void ThrowWhenSecretNotFound()
    {
        // act & assert
        Assert.Throws<SecretNotFoundException>(
            () =>
            {
                new ConfigurationBuilder().AddSecretsManager(
                        "w4k/awssm/unknown-secret-mandatory",
                        SecretsManagerTestFixture.SecretsManagerClient)
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
                config = new ConfigurationBuilder().AddSecretsManager(
                        "w4k/awssm/unknown-secret-optional",
                        SecretsManagerTestFixture.SecretsManagerClient,
                        c => c.IsOptional = true)
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
        var config = new ConfigurationBuilder().AddSecretsManager(
                SecretsManagerTestFixture.KeyValueSecretName,
                SecretsManagerTestFixture.SecretsManagerClient)
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
        var config = new ConfigurationBuilder().AddSecretsManager(
                SecretsManagerTestFixture.KeyValueSecretName,
                SecretsManagerTestFixture.SecretsManagerClient,
                c => c.ConfigurationKeyPrefix = "App:Secrets")
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
        var config = new ConfigurationBuilder().AddSecretsManager(
                SecretsManagerTestFixture.KeyValueSecretName,
                SecretsManagerTestFixture.SecretsManagerClient,
                c => c.KeyTransformers.Add(customKeyTransformer))
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
        var config = new ConfigurationBuilder().AddSecretsManager(
                SecretsManagerTestFixture.ComplexSecretName,
                SecretsManagerTestFixture.SecretsManagerClient)
            .Build();

        var secrets = config.AsEnumerable().ToList();

        // assert
        Assert.That(secrets, Is.EquivalentTo(expected));
    }

    [Test]
    public void FetchMultipleSecrets()
    {
        // act
        var config = new ConfigurationBuilder().AddSecretsManager(
                [SecretsManagerTestFixture.KeyValueSecretName, SecretsManagerTestFixture.ComplexSecretName],
                SecretsManagerTestFixture.SecretsManagerClient)
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