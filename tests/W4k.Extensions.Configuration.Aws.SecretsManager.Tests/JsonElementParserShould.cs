using W4k.Extensions.Configuration.Aws.SecretsManager.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class JsonElementParserShould
{
    [Test]
    public async Task ParseJsonValueWithCommentAndTrailingComma()
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

        await Assert.That(result).IsTrue();
        await Assert.That(jsonElement.GetProperty("name").GetString()).IsEqualTo("James Bond");
        await Assert.That(jsonElement.GetProperty("gadgets").GetArrayLength()).IsEqualTo(3);
    }

    [Test]
    [MethodDataSource(nameof(GenerateInvalidJsonValues))]
    public async Task NotParseInvalidJsonValue(string input)
    {
        var parser = new JsonElementParser();
        var result = parser.TryParse(input, out _);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task NotParseNonJsonValue()
    {
        var invalidSecret = "This is not a JSON string.";

        var parser = new JsonElementParser();
        var result = parser.TryParse(invalidSecret, out _);

        await Assert.That(result).IsFalse();
    }

    public static IEnumerable<string> GenerateInvalidJsonValues()
    {
        yield return "";
        yield return "{";
        yield return "[";
        yield return "{]";
        yield return "  ]  ";
        yield return """
            {
                "key": Well, this doesn't really work as JSON, does it?
            }
            """;
    }
}