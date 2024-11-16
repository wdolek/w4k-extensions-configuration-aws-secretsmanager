using System.Text;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretFetcherShould
{
    [Test]
    public async Task ReturnStringSecret()
    {
        // arrange
        var secretId = "secret123";
        var versionId = "version9000";
        var secretString = """{ "le_secret": "MZ/X" }""";

        var getSecretValueResponse = new GetSecretValueResponse
        {
            VersionId = versionId,
            SecretString = secretString,
        };

        var secretsManager = Substitute.For<IAmazonSecretsManager>();
        secretsManager
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(getSecretValueResponse);

        var secretFetcher = new SecretFetcher(secretsManager);

        // act
        var result = await secretFetcher.GetSecret(secretId, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.VersionId, Is.EqualTo(versionId));
            Assert.That(result.Value, Is.EqualTo(secretString));
        });
    }

    [Test]
    public async Task ReturnBinarySecret()
    {
        // arrange
        var secretId = "secret123";
        var versionId = "version9000";

        var secretContent = """{ "le_secret": "MZ/X" }""";
        var secretContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(secretContent));
        using var secretBinary = new MemoryStream(Encoding.UTF8.GetBytes(secretContentBase64));

        var getSecretValueResponse = new GetSecretValueResponse
        {
            VersionId = versionId,
            SecretBinary = secretBinary,
        };

        var secretsManager = Substitute.For<IAmazonSecretsManager>();
        secretsManager
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(getSecretValueResponse);

        var secretFetcher = new SecretFetcher(secretsManager);

        // act
        var result = await secretFetcher.GetSecret(secretId, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.VersionId, Is.EqualTo(versionId));
            Assert.That(result.Value, Is.EqualTo(secretContent));
        });
    }

    [Test]
    public void ThrowIfSecretIsNeitherStringOrBinary()
    {
        // arrange
        var secretsManager = Substitute.For<IAmazonSecretsManager>();
        secretsManager
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse());

        var secretFetcher = new SecretFetcher(secretsManager);

        // act & assert
        Assert.ThrowsAsync<SecretRetrievalException>(async () => await secretFetcher.GetSecret("secret123", null, CancellationToken.None));
    }

    [Test]
    public void ThrowIfSecretNotFound()
    {
        // arrange
        var secretsManager = Substitute.For<IAmazonSecretsManager>();
        secretsManager
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Throws(new ResourceNotFoundException("Secret not found"));

        var secretFetcher = new SecretFetcher(secretsManager);

        // act & assert
        Assert.ThrowsAsync<SecretNotFoundException>(async () => await secretFetcher.GetSecret("secret123", null, CancellationToken.None));
    }

    [Test]
    public async Task PassVersionParameters()
    {
        // arrange
        var secretId = "secret123";
        var versionId = "version9000";
        var versionStage = "stage123";

        var secretVersion = new SecretVersion
        {
            VersionId = versionId,
            VersionStage = versionStage
        };

        var secretsManager = Substitute.For<IAmazonSecretsManager>();
        secretsManager
            .GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { VersionId = "d6d1b757d46d449d1835a10869dfb9d1", SecretString = "L3_S3cr37" });

        var secretFetcher = new SecretFetcher(secretsManager);

        // act
        await secretFetcher.GetSecret(secretId, secretVersion, CancellationToken.None);

        // assert
        await secretsManager
            .Received()
            .GetSecretValueAsync(
                Arg.Is<GetSecretValueRequest>(
                    r => r.SecretId == secretId
                         && r.VersionId == versionId
                         && r.VersionStage == versionStage),
                Arg.Any<CancellationToken>());
    }
}