namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretProcessorShould
{
    private static readonly SecretsManagerConfigurationProviderOptions TestOptions = new("le-secret");

    [Test]
    public void ThrowWhenUnableToParse()
    {
        // arrange
        var secretString = "<xml>definitely not a JSON</xml>";
        var processor = SecretsProcessor.Json;

        // act & assert
        Assert.Throws<FormatException>(() => processor.GetConfigurationData(TestOptions, secretString));
    }

    [Test]
    public void ExecuteTransformationForEachKeyValuePair()
    {
        // arrange
        var secretString = """
            {
                "App__Misc_Settings__Key": "Value1"
            }
            """;

        var processor = SecretsProcessor.Json;

        // act
        var data = processor.GetConfigurationData(TestOptions, secretString);

        // assert
        Assert.That(data, Has.Count.EqualTo(1));
        Assert.That(data.Keys.Single(), Is.EqualTo("App:Misc_Settings:Key"));
    }
}