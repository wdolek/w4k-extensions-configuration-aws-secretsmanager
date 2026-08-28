namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class KeyDelimiterTransformerShould
{
    [Test]
    public async Task ReplaceDoubleUnderscoreWithKeyDelimiter()
    {
        var transformer = new KeyDelimiterTransformer();
        var result = transformer.Transform("App__Settings__Key");

        await Assert.That(result).IsEqualTo("App:Settings:Key");
    }
}