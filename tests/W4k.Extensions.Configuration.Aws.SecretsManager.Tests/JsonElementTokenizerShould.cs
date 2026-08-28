using System.Text.Json;
using W4k.Extensions.Configuration.Aws.SecretsManager.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class JsonElementTokenizerShould
{
    [Test]
    public async Task NotAddDelimiterWhenPrefixIsEmpty()
    {
        // arrange
        var json = """
            {
                "key": "value"
            }
            """;

        var jsonElement = JsonDocument.Parse(json).RootElement;
        var tokenizer = new JsonElementTokenizer();

        // act
        var result = tokenizer
            .Tokenize(jsonElement, "")
            .ToList();

        // assert
        await Assert.That(result[0].Key).IsEqualTo("key");
    }

    [Test]
    public async Task ReturnKeyValuePairsWhenTokenizingObject()
    {
        // arrange
        var json = """
            {
                "name": "James Bond",
                "age": 45,
                "gadgets": [
                    "Jetpack",
                    "Lotus Esprit S1",
                    "Dentonite Toothpaste"
                ],
                "hasLicenseToKill": true,
                "married": null,
                "permissions": {
                    "kill": true
                }
            }
            """;

        var jsonElement = JsonDocument.Parse(json).RootElement;
        var tokenizer = new JsonElementTokenizer();

        // act
        var result = tokenizer
            .Tokenize(jsonElement, "MI6")
            .ToList();

        // assert
        await Assert.That(result).Count().IsEqualTo(8);
        await Assert.That(result[0].Key).IsEqualTo("MI6:name");
        await Assert.That(result[0].Value).IsEqualTo("James Bond");
        await Assert.That(result[1].Key).IsEqualTo("MI6:age");
        await Assert.That(result[1].Value).IsEqualTo("45");
        await Assert.That(result[2].Key).IsEqualTo("MI6:gadgets:0");
        await Assert.That(result[2].Value).IsEqualTo("Jetpack");
        await Assert.That(result[3].Key).IsEqualTo("MI6:gadgets:1");
        await Assert.That(result[3].Value).IsEqualTo("Lotus Esprit S1");
        await Assert.That(result[4].Key).IsEqualTo("MI6:gadgets:2");
        await Assert.That(result[4].Value).IsEqualTo("Dentonite Toothpaste");
        await Assert.That(result[5].Key).IsEqualTo("MI6:hasLicenseToKill");
        await Assert.That(result[5].Value).IsEqualTo("True");
        await Assert.That(result[6].Key).IsEqualTo("MI6:married");
        await Assert.That(result[6].Value).IsNull();
        await Assert.That(result[7].Key).IsEqualTo("MI6:permissions:kill");
        await Assert.That(result[7].Value).IsEqualTo("True");
    }

    [Test]
    public async Task NotTokenizeJsonStringValue()
    {
        // arrange
        var json = """
            {
                "key": "{ \"subkey\": \"value\" }"
            }
            """;

        var jsonElement = JsonDocument.Parse(json).RootElement;
        var tokenizer = new JsonElementTokenizer();

        // act
        var result = tokenizer
            .Tokenize(jsonElement, "")
            .ToList();

        // assert
        await Assert.That(result[0].Key).IsEqualTo("key");
        await Assert.That(result[0].Value).IsEqualTo("{ \"subkey\": \"value\" }");
    }
}