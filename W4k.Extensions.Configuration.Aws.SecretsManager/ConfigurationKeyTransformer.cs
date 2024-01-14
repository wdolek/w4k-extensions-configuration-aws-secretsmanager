using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public interface IConfigurationKeyTransformer
{
    string Transform(string key);
}

public sealed class KeyDelimiterTransformer : IConfigurationKeyTransformer
{
    public static readonly IConfigurationKeyTransformer Instance = new KeyDelimiterTransformer();
    public string Transform(string key) => key.Replace("__", ConfigurationPath.KeyDelimiter);
}