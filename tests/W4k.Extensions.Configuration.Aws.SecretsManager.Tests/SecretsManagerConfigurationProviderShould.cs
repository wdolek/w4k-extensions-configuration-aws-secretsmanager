using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

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
    public async Task LoadSecret()
    {
        // arrange
        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(InitialSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        provider.Load();

        // assert
        var hasKey = provider.TryGet("AppSettingsKey", out var value);
        await Assert.That(hasKey).IsTrue();
        await Assert.That(value).IsEqualTo("Value");
    }

    [Test]
    public async Task ThrowWhenLoadingFails()
    {
        // arrange
        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Throws(new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act & assert
        var ex = await Assert.That(() => provider.Load()).ThrowsExactly<SecretRetrievalException>();
        await Assert.That(ex!.InnerException).IsNotNull();
        await Assert.That(ex!.InnerException).IsTypeOf<ResourceNotFoundException>();
    }

    [Test]
    public async Task NotThrowWhenLoadingFailsWithIgnoringLoadException()
    {
        // arrange
        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Throws(new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = secretsManagerStub.Object,
            OnLoadException = ctx => { ctx.Ignore = true; }
        };

        var provider = new SecretsManagerConfigurationProvider(source);

        // act & assert
        await Assert.That(() => provider.Load()).ThrowsNothing();
    }

    [Test]
    public async Task ThrowWhenReloadFails()
    {
        // arrange
        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(
                InitialSecretValueResponse)
            .Then()
            .Throws(new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = secretsManagerStub.Object
        };

        var provider = new SecretsManagerConfigurationProvider(source);

        // act & assert
        // 1. execute initial load
        provider.Load();

        // 2. execute reload
        var ex = await Assert.That(() => provider.Reload()).ThrowsExactly<SecretRetrievalException>();
        await Assert.That(ex!.InnerException).IsNotNull();
        await Assert.That(ex!.InnerException).IsTypeOf<ResourceNotFoundException>();
    }

    [Test]
    public async Task NotThrowWhenReloadFailsWithIgnoringReloadException()
    {
        // arrange
        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(
                InitialSecretValueResponse)
            .Then()
            .Throws(new ResourceNotFoundException("(╯‵□′)╯︵┻━┻"));

        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            SecretsManager = secretsManagerStub.Object,
            OnReloadException = ctx => { ctx.Ignore = true; }
        };

        var provider = new SecretsManagerConfigurationProvider(source);

        // act & assert
        // 1. execute initial load
        provider.Load();

        // 2. execute reload
        await Assert.That(() => provider.Reload()).ThrowsNothing();
    }

    [Test]
    public async Task NotifyRefreshChangeOnNewValue()
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

        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .ReturnsSequentially(InitialSecretValueResponse, newSecretsResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        // 1. execute initial load
        provider.Load();

        // 2. execute reload
        var reloadToken = provider.GetReloadToken();
        provider.Reload();

        // assert
        await Assert.That(reloadToken.HasChanged).IsTrue();

        var hasKey = provider.TryGet("NewSettingsKey", out var value);
        await Assert.That(hasKey).IsTrue();
        await Assert.That(value).IsEqualTo("New value");
    }

    [Test]
    public async Task NotNotifyRefreshChangeOnSameValue()
    {
        // arrange
        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .ReturnsSequentially(InitialSecretValueResponse, InitialSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub.Object };
        var provider = new SecretsManagerConfigurationProvider(source);

        // act
        // 1. execute initial load
        provider.Load();

        // 2. execute reload
        var reloadToken = provider.GetReloadToken();
        provider.Reload();

        // assert
        await Assert.That(reloadToken.HasChanged).IsFalse();
    }

    [Test]
    public async Task NotApplyKeyTransformersAddedAfterBuild()
    {
        // arrange
        var initialSecretValueResponse = new GetSecretValueResponse
        {
            VersionId = "d6d1b757d46d449d1835a10869dfb9d1",
            SecretString = """
                {
                    "AppSettings__Key": "Value"
                }
                """
        };

        var newSecretValueResponse = new GetSecretValueResponse
        {
            VersionId = "d6d1b757d46d449d1835a10869dfb9d2",
            SecretString = """
                {
                    "AppSettings__Key": "New value"
                }
                """
        };

        var secretsManagerStub = IAmazonSecretsManager.Mock();
        secretsManagerStub
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .ReturnsSequentially(initialSecretValueResponse, newSecretValueResponse);

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub.Object };
        var provider = (SecretsManagerConfigurationProvider)source.Build(new ConfigurationBuilder());

        // act
        provider.Load();

        // mutating key transformers after the source was built must not affect reloads
        source.KeyTransformers.Clear();

        provider.Reload();

        // assert
        // - transformed key is still present, with the new value
        var hasKey = provider.TryGet("AppSettings:Key", out var value);
        await Assert.That(hasKey).IsTrue();
        await Assert.That(value).IsEqualTo("New value");

        // - untransformed key was not introduced
        await Assert.That(provider.TryGet("AppSettings__Key", out _)).IsFalse();
    }
}