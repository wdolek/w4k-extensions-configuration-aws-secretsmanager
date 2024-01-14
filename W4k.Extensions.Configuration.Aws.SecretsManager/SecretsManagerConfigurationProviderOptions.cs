namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretsManagerConfigurationProviderOptions
{
    private string _keyPrefix = "";
    private ISecretsProcessor _processor = SecretsProcessor.Json;
    
    public required string SecretId { get; init; }
    public SecretVersionBase? Version { get; set; }

    public string ConfigurationKeyPrefix
    {
        get { return _keyPrefix; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _keyPrefix = value;
        }
    }

    public ISecretsProcessor Processor
    {
        get { return _processor; }
        set
        {
            ArgumentNullException.ThrowIfNull(_processor);
            _processor = value;
        }
    }

    public List<IConfigurationKeyTransformer> KeyTransformers { get; } = [KeyDelimiterTransformer.Instance];
}

public abstract class SecretVersionBase;

public sealed class SecretVersion : SecretVersionBase
{
    public required string Id { get; init; } 
}

public sealed class StagedSecretVersion : SecretVersionBase
{
    public static readonly StagedSecretVersion Current = new() { Stage = "AWSCURRENT" };
    public static readonly StagedSecretVersion Previous = new() { Stage = "AWSPREVIOUS" };

    public required string Stage { get; init; }
}