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
}