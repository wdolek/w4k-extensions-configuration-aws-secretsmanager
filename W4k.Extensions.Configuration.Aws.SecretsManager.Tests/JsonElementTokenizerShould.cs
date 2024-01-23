using System.Text.Json;
using W4k.Extensions.Configuration.Aws.SecretsManager.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class JsonElementTokenizerShould
{
    [Test]
    public void NotAddDelimiterWhenPrefixIsEmpty()
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
        Assert.That(result[0].Key, Is.EqualTo("key"));
    }

    [Test]
    public void ReturnKeyValuePairsWhenTokenizingObject()
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
                "married": null
            }
            """;

        var jsonElement = JsonDocument.Parse(json).RootElement;
        var tokenizer = new JsonElementTokenizer();
        
        // act
        var result = tokenizer
            .Tokenize(jsonElement, "MI6")
            .ToList();

        // assert
        Assert.That(result, Has.Count.EqualTo(7));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].Key, Is.EqualTo("MI6:name"));
            Assert.That(result[0].Value, Is.EqualTo("James Bond"));
            
            Assert.That(result[1].Key, Is.EqualTo("MI6:age"));
            Assert.That(result[1].Value, Is.EqualTo("45"));

            Assert.That(result[2].Key, Is.EqualTo("MI6:gadgets:0"));
            Assert.That(result[2].Value, Is.EqualTo("Jetpack"));

            Assert.That(result[3].Key, Is.EqualTo("MI6:gadgets:1"));
            Assert.That(result[3].Value, Is.EqualTo("Lotus Esprit S1"));

            Assert.That(result[4].Key, Is.EqualTo("MI6:gadgets:2"));
            Assert.That(result[4].Value, Is.EqualTo("Dentonite Toothpaste"));

            Assert.That(result[5].Key, Is.EqualTo("MI6:hasLicenseToKill"));
            Assert.That(result[5].Value, Is.EqualTo("True"));
            
            Assert.That(result[6].Key, Is.EqualTo("MI6:married"));
            Assert.That(result[6].Value, Is.Null);
        });
    }
}