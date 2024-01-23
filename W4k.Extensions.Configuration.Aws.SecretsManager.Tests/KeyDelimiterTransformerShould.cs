namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class KeyDelimiterTransformerShould
{
    [Test]
    public void ReplaceDoubleUnderscoreWithKeyDelimiter()
    {
        var transformer = new KeyDelimiterTransformer();
        var result = transformer.Transform("App__Settings__Key");

        Assert.That(result, Is.EqualTo("App:Settings:Key"));
    }
}