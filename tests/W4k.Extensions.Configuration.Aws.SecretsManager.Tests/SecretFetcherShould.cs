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

        var secretsManager = IAmazonSecretsManager.Mock();
        secretsManager
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(getSecretValueResponse);

        var secretFetcher = new SecretFetcher(secretsManager.Object);

        // act
        var result = await secretFetcher.GetSecret(secretId, null, CancellationToken.None);

        await Assert.That(result.VersionId).IsEqualTo(versionId);
        await Assert.That(result.Value).IsEqualTo(secretString);
    }

    [Test]
    public async Task ReturnBinarySecret()
    {
        // arrange
        var secretId = "secret123";
        var versionId = "version9000";

        var secretContent = """{ "le_secret": "MZ/X" }""";
        var secretBytes = Encoding.UTF8.GetBytes(secretContent);

        // the AWS SDK constructs the stream with `publiclyVisible: true` (see `MemoryStreamUnmarshaller`), mimic it
        using var secretBinary = new MemoryStream(secretBytes, 0, secretBytes.Length, writable: true, publiclyVisible: true);

        var getSecretValueResponse = new GetSecretValueResponse
        {
            VersionId = versionId,
            SecretBinary = secretBinary,
        };

        var secretsManager = IAmazonSecretsManager.Mock();
        secretsManager
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(getSecretValueResponse);

        var secretFetcher = new SecretFetcher(secretsManager.Object);

        // act
        var result = await secretFetcher.GetSecret(secretId, null, CancellationToken.None);

        await Assert.That(result.VersionId).IsEqualTo(versionId);
        await Assert.That(result.Value).IsEqualTo(secretContent);
    }

    [Test]
    public async Task ThrowIfBinarySecretIsNotValidUtf8()
    {
        // arrange
        var secretId = "secret123";

        // 0xC3 0x28 is an invalid UTF-8 sequence (continuation byte expected)
        var secretBytes = new byte[] { 0xC3, 0x28 };

        // the AWS SDK constructs the stream with `publiclyVisible: true` (see `MemoryStreamUnmarshaller`), mimic it
        using var secretBinary = new MemoryStream(secretBytes, 0, secretBytes.Length, writable: true, publiclyVisible: true);

        var getSecretValueResponse = new GetSecretValueResponse
        {
            VersionId = "d6d1b757d46d449d1835a10869dfb9d1",
            SecretBinary = secretBinary,
        };

        var secretsManager = IAmazonSecretsManager.Mock();
        secretsManager
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(getSecretValueResponse);

        var secretFetcher = new SecretFetcher(secretsManager.Object);

        // act & assert
        // the decode failure must be reported without leaking any part of the payload:
        // - a `SecretRetrievalException` naming only the secret id, not the raw `DecoderFallbackException`
        //   (whose default message embeds the offending bytes, e.g. "Unable to translate bytes [C3]...")
        // - no inner exception at all, so nothing upstream can print the leaky message via `Exception.ToString()`
        var ex = await Assert.That(async () => await secretFetcher.GetSecret(secretId, null, CancellationToken.None))
            .ThrowsExactly<SecretRetrievalException>();

        await Assert.That(ex!.SecretName).IsEqualTo(secretId);
        await Assert.That(ex.InnerException).IsNull();
        await Assert.That(ex.Message).DoesNotContain("0xC3");
        await Assert.That(ex.Message).DoesNotContain("[C3]");
    }

    [Test]
    public async Task ThrowIfSecretIsNeitherStringOrBinary()
    {
        // arrange
        var secretsManager = IAmazonSecretsManager.Mock();
        secretsManager
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(new GetSecretValueResponse());

        var secretFetcher = new SecretFetcher(secretsManager.Object);

        // act & assert
        await Assert.That(async () => await secretFetcher.GetSecret("secret123", null, CancellationToken.None)).ThrowsExactly<SecretRetrievalException>();
    }

    [Test]
    public async Task ThrowIfSecretNotFound()
    {
        // arrange
        var secretsManager = IAmazonSecretsManager.Mock();
        secretsManager
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Throws(new ResourceNotFoundException("Secret not found"));

        var secretFetcher = new SecretFetcher(secretsManager.Object);

        // act & assert
        await Assert.That(async () => await secretFetcher.GetSecret("secret123", null, CancellationToken.None)).ThrowsExactly<ResourceNotFoundException>();
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

        var secretsManager = IAmazonSecretsManager.Mock();
        secretsManager
            .GetSecretValueAsync(Any<GetSecretValueRequest>(), Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { VersionId = "d6d1b757d46d449d1835a10869dfb9d1", SecretString = "L3_S3cr37" });

        var secretFetcher = new SecretFetcher(secretsManager.Object);

        // act
        await secretFetcher.GetSecret(secretId, secretVersion, CancellationToken.None);

        // assert
        secretsManager
            .GetSecretValueAsync(
                Is<GetSecretValueRequest>(
                    r => r!.SecretId == secretId
                         && r.VersionId == versionId
                         && r.VersionStage == versionStage),
                Any<CancellationToken>())
            .WasCalled();
    }
}