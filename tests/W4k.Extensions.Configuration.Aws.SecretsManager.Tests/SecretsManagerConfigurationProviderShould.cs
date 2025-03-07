﻿using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerConfigurationProviderShould
{
    private static readonly GetSecretValueResponse InitialSecretValueResponse = new()
    {
        VersionId = "d6d1b757d46d449d1835a10869dfb9d1",
        SecretString = """
            {
                "AppSettingsKey": "Value"
            }
            """
    };

    [Test]
    public void LoadSecret()
    {
        // arrange
        var secretsManagerStub = Substitute.For<IAmazonSecretsManager>();
        secretsManagerStub
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(InitialSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();

        // assert
        var hasKey = provider.TryGet("AppSettingsKey", out var value);
        Assert.Multiple(() =>
        {
            Assert.That(hasKey, Is.True);
            Assert.That(value, Is.EqualTo("Value"));
        });
    }

    [Test]
    public void ThrowWhenLoadingFails()
    {
        // arrange
        var secretsManagerStub = Substitute.For<IAmazonSecretsManager>();
        secretsManagerStub
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Throws(new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act & assert
        var ex = Assert.Throws<SecretRetrievalException>(() => provider.Load());
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException, Is.TypeOf<ResourceNotFoundException>());
    }

    [Test]
    public void NotThrowWhenLoadingFailsWithIgnoringLoadException()
    {
        // arrange
        var secretsManagerStub = Substitute.For<IAmazonSecretsManager>();
        secretsManagerStub
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Throws(new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = secretsManagerStub,
            OnLoadException = ctx => { ctx.Ignore = true; }
        };

        var provider = new SecretsManagerConfigurationProvider(source);

        // act & assert
        Assert.DoesNotThrow(() => provider.Load());
    }

    [Test]
    public void ThrowWhenReloadFails()
    {
        // arrange
        var secretsManagerStub = Substitute.For<IAmazonSecretsManager>();
        secretsManagerStub
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => InitialSecretValueResponse,
                _ => throw new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = secretsManagerStub
        };

        var provider = new SecretsManagerConfigurationProvider(source);

        // act & assert
        // 1. execute initial load
        provider.Load();

        // 2. execute reload
        var ex = Assert.Throws<SecretRetrievalException>(() => provider.Reload());
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException, Is.TypeOf<ResourceNotFoundException>());
    }

    [Test]
    public void NotThrowWhenReloadFailsWithIgnoringReloadException()
    {
        // arrange
        var secretsManagerStub = Substitute.For<IAmazonSecretsManager>();
        secretsManagerStub
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => InitialSecretValueResponse,
                _ => throw new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = secretsManagerStub,
            OnReloadException = ctx => { ctx.Ignore = true; }
        };

        var provider = new SecretsManagerConfigurationProvider(source);

        // act & assert
        // 1. execute initial load
        provider.Load();

        // 2. execute reload
        Assert.DoesNotThrow(() => provider.Reload());
    }

    [Test]
    public void NotifyRefreshChangeOnNewValue()
    {
        // arrange
        var newSecretsResponse = new GetSecretValueResponse
        {
            VersionId = "d6d1b757d46d449d1835a10869dfb9d2",
            SecretString = """
                {
                    "AppSettingsKey": "Second value",
                    "NewSettingsKey": "New value"
                }
                """
        };

        var secretsManagerStub = Substitute.For<IAmazonSecretsManager>();
        secretsManagerStub
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                InitialSecretValueResponse,
                newSecretsResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        // 1. execute initial load
        provider.Load();

        // 2. execute reload
        var reloadToken = provider.GetReloadToken();
        provider.Reload();

        // assert
        Assert.That(reloadToken.HasChanged, Is.True);

        var hasKey = provider.TryGet("NewSettingsKey", out var value);
        Assert.Multiple(() =>
        {
            Assert.That(hasKey, Is.True);
            Assert.That(value, Is.EqualTo("New value"));
        });
    }

    [Test]
    public void NotNotifyRefreshChangeOnSameValue()
    {
        // arrange
        var secretsManagerStub = Substitute.For<IAmazonSecretsManager>();
        secretsManagerStub
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                InitialSecretValueResponse,
                InitialSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        // 1. execute initial load
        provider.Load();

        // 2. execute reload
        var reloadToken = provider.GetReloadToken();
        provider.Reload();

        // assert
        Assert.That(reloadToken.HasChanged, Is.False);
    }
}