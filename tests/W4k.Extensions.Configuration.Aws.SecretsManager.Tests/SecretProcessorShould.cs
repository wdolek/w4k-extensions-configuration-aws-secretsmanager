using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretProcessorShould
{
    private static readonly SecretsManagerConfigurationSource ConfigSource = new() { SecretName = "le-secret" };

    [Test]
    public async Task ThrowWhenUnableToParse()
    {
        // arrange
        var secretString = "<xml>definitely not a JSON</xml>";
        var processor = SecretsProcessor.Json;

        // act & assert
        await Assert.That(() => processor.GetConfigurationData(ConfigSource, secretString)).ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task ExecuteTransformationForEachKeyValuePair()
    {
        // arrange
        var secretString = """
            {
                "App__Misc_Settings__Key": "Value1"
            }
            """;

        var processor = SecretsProcessor.Json;

        // act
        var data = processor.GetConfigurationData(ConfigSource, secretString);

        // assert
        await Assert.That(data).Count().IsEqualTo(1);
        await Assert.That(data.Keys.Single()).IsEqualTo("App:Misc_Settings:Key");
    }

    [Test]
    public async Task UseKeyTransformersSnapshottedAtBuildTime()
    {
        // arrange
        var secretsManagerStub = IAmazonSecretsManager.Mock();

        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret", SecretsManager = secretsManagerStub.Object };
        source.Build(new ConfigurationBuilder());

        // act
        // mutating key transformers after the source was built must not affect processing
        source.KeyTransformers.Clear();

        var data = SecretsProcessor.Json.GetConfigurationData(
            source,
            """
            {
                "App__Key": "Value"
            }
            """);

        // assert
        // transformation in effect at build time is still applied
        await Assert.That(data.Keys.Single()).IsEqualTo("App:Key");
    }

    [Test]
    public async Task PlacePlainTextValueUnderExplicitKeyWithPrefix()
    {
        // arrange
        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            ConfigurationKeyPrefix = "App:Secrets",
        };
        var processor = new PlainTextSecretProcessor("ApiKey");

        // act
        var data = processor.GetConfigurationData(source, "le_value");

        // assert
        await Assert.That(data.Keys.Single()).IsEqualTo("App:Secrets:ApiKey");
        await Assert.That(data.Values.Single()).IsEqualTo("le_value");
    }

    [Test]
    public async Task PlacePlainTextValueUnderExplicitKeyWithoutPrefix()
    {
        // arrange
        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret" };
        var processor = new PlainTextSecretProcessor("ApiKey");

        // act
        var data = processor.GetConfigurationData(source, "le_value");

        // assert
        await Assert.That(data.Keys.Single()).IsEqualTo("ApiKey");
        await Assert.That(data.Values.Single()).IsEqualTo("le_value");
    }

    [Test]
    public async Task PlacePlainTextValueUnderConfigurationKeyPrefix()
    {
        // arrange
        var source = new SecretsManagerConfigurationSource
        {
            SecretName = "le-secret",
            ConfigurationKeyPrefix = "MySecret",
        };

        // act
        var data = SecretsProcessor.PlainText.GetConfigurationData(source, "le_value");

        // assert
        await Assert.That(data.Keys.Single()).IsEqualTo("MySecret");
        await Assert.That(data.Values.Single()).IsEqualTo("le_value");
    }

    [Test]
    public async Task ThrowWhenPlainTextValueHasNeitherKeyNorPrefix()
    {
        // arrange
        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret" };
        var processor = SecretsProcessor.PlainText;

        // act & assert
        var ex = await Assert.That(() => processor.GetConfigurationData(source, "le_value")).ThrowsExactly<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("Configuration key is not set");
    }

    [Test]
    public async Task ThrowWhenPlainTextExplicitKeyIsWhitespaceOnly()
    {
        // act & assert
        await Assert.That(() => new PlainTextSecretProcessor(" ")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ApplyKeyTransformersToPlainTextKey()
    {
        // arrange
        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret" };
        var processor = new PlainTextSecretProcessor("Api__Key");

        // act
        var data = processor.GetConfigurationData(source, "le_value");

        // assert
        await Assert.That(data.Keys.Single()).IsEqualTo("Api:Key");
    }

    [Test]
    public async Task PreservePlainTextValueTrailingWhitespace()
    {
        // arrange
        var source = new SecretsManagerConfigurationSource { SecretName = "le-secret" };
        var processor = new PlainTextSecretProcessor("ApiKey");
        var secretString = "le_value\n";

        // act
        var data = processor.GetConfigurationData(source, secretString);

        // assert
        await Assert.That(data.Values.Single()).IsEqualTo("le_value\n");
    }
}