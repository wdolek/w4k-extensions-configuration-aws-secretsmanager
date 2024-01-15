namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Configuration of <see cref="SecretsManagerConfigurationProvider"/>.
/// </summary>
public class SecretsManagerConfigurationProviderOptions
{
    private string _keyPrefix = "";
    private ISecretsProcessor _processor = SecretsProcessor.Json;

    /// <summary>
    /// Gets or sets secret ID/name to fetch.
    /// </summary>
#if NET8_0_OR_GREATER
    public required string SecretId { get; init; }
#else
    public string SecretId { get; init; } = null!;
#endif

    /// <summary>
    /// Gets or sets secret version to fetch, if not provided, latest version of secret is fetched.
    /// </summary>
    public SecretVersionBase? Version { get; set; }

    /// <summary>
    /// Configuration key prefix, if not set, secret properties are placed directly in configuration root.
    /// </summary>
    public string ConfigurationKeyPrefix
    {
        get { return _keyPrefix; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _keyPrefix = value;
        }
    }

    /// <summary>
    /// Secrets processor (parsing, tokenizing), default is <see cref="SecretsProcessor.Json"/>.
    /// </summary>
    public ISecretsProcessor Processor
    {
        get { return _processor; }
        set
        {
            ArgumentNullException.ThrowIfNull(_processor);
            _processor = value;
        }
    }

    /// <summary>
    /// Configuration key transformers applied after tokenization. By default, only <see cref="KeyDelimiterTransformer"/> is present.
    /// </summary>
    public List<IConfigurationKeyTransformer> KeyTransformers { get; } = new() { new KeyDelimiterTransformer() };
}

/// <summary>
/// Representation of secret version.
/// </summary>
public abstract class SecretVersionBase
{
}

/// <inheritdoc/>
public sealed class SecretVersion : SecretVersionBase
{
    /// <summary>
    /// Gets or sets secret version ID.
    /// </summary>
#if NET8_0_OR_GREATER
    public required string Id { get; init; }
#else
    public string? Id { get; init; }
#endif
}

/// <inheritdoc/>
public sealed class StagedSecretVersion : SecretVersionBase
{
    /// <summary>
    /// Secret version for <c>AWSCURRENT</c> stage.
    /// </summary>
    public static readonly StagedSecretVersion Current = new() { Stage = "AWSCURRENT" };
    
    /// <summary>
    /// Secret version for <c>AWSPREVIOUS</c> stage.
    /// </summary>
    public static readonly StagedSecretVersion Previous = new() { Stage = "AWSPREVIOUS" };

    /// <summary>
    /// Gets or sets custom stage name.
    /// </summary>
#if NET8_0_OR_GREATER
    public required string Stage { get; init; }
#else
    public string? Stage { get; init; }
#endif
}