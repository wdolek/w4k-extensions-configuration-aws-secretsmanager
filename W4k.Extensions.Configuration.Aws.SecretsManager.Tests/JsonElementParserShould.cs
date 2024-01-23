using W4k.Extensions.Configuration.Aws.SecretsManager.Json;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

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

    [TestCaseSource(nameof(GenerateInvalidJsonValues))]
    public void NotParseInvalidJsonValue(string input)
    {
        var parser = new JsonElementParser();
        var result = parser.TryParse(input, out _);

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

    public static IEnumerable<TestCaseData> GenerateInvalidJsonValues()
    {
        yield return new TestCaseData("");

        yield return new TestCaseData("{");
        yield return new TestCaseData("[");
        yield return new TestCaseData("{]");
        yield return new TestCaseData("  ]  ");

        yield return new TestCaseData(
            """
            {
                "key": Well, this doesn't really work as JSON, does it?
            }
            """);
    }
}