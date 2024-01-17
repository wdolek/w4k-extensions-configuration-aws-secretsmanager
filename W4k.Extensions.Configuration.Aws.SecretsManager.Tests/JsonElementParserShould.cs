using W4k.Extensions.Configuration.Aws.SecretsManager.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Tests;

public class JsonElementParserShould
{
    [Test]
    public void ParseJsonValueWithCommentAndTrailingComma()
    {
        var secret = """
{
    // secret agent name
    "name": "James Bond",
    "gadgets": [
        "Jetpack",
        "Lotus Esprit S1",
        "Dentonite Toothpaste",
    ]
}
""";

        var parser = new JsonElementParser();
        var result = parser.TryParse(secret, out var jsonElement);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(jsonElement.GetProperty("name").GetString(), Is.EqualTo("James Bond"));
            Assert.That(jsonElement.GetProperty("gadgets").GetArrayLength(), Is.EqualTo(3));
        });
    }

    [Test]
    public void NotParseInvalidJsonValue()
    {
        var invalidSecret = """
{
    "key": Well, this doesn't really work as JSON, does it?
}
""";

        var parser = new JsonElementParser();
        var result = parser.TryParse(invalidSecret, out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void NotParseNonJsonValue()
    {
        var invalidSecret = "This is not a JSON string.";

        var parser = new JsonElementParser();
        var result = parser.TryParse(invalidSecret, out _);

        Assert.That(result, Is.False);
    }
}