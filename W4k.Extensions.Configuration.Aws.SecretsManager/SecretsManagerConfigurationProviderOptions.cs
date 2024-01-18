namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Configuration of <see cref="SecretsManagerConfigurationProvider"/>.
/// </summary>
public class SecretsManagerConfigurationProviderOptions
{
    private string _keyPrefix = "";
    private ISecretProcessor _processor = SecretsProcessor.Json;
    private StartupOptions _startupOptions = new StartupOptions();

    /// <summary>
    /// Gets or sets secret name (or its complete ARN) to fetch.
    /// </summary>
#if NET8_0_OR_GREATER
    public required string SecretName { get; init; }
#else
    public string SecretName { get; init; } = null!;
#endif

    /// <summary>
    /// Gets or sets secret version to fetch, if not provided, latest version of secret is fetched.
    /// </summary>
    public SecretVersion? Version { get; set; }

    /// <summary>
    /// Gets or sets configuration key prefix, if not set, secret properties are placed directly in configuration root.
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
    /// Gets or sets secrets processor (parsing, tokenizing), default is <see cref="SecretsProcessor.Json"/>.
    /// </summary>
    public ISecretProcessor Processor
    {
        get { return _processor; }
        set
        {
            ArgumentNullException.ThrowIfNull(_processor);
            _processor = value;
        }
    }

    /// <summary>
    /// Gets list of configuration key transformers applied after tokenization.
    /// </summary>
    /// <remarks>
    /// Transformers are applied in the order they are added to the list.
    /// Result of previous transformer is input for the next one.
    /// By default, only <see cref="KeyDelimiterTransformer"/> is present.
    /// </remarks>
    public List<IConfigurationKeyTransformer> KeyTransformers { get; } = new() { new KeyDelimiterTransformer() };
    
    /// <summary>
    /// Gets or sets configuration change watcher.
    /// </summary>
    public IConfigurationWatcher? ConfigurationWatcher { get; set; }

    /// <summary>
    /// Gets options of initial secret load (at startup).
    /// </summary>
    public StartupOptions Startup
    {
        get { return _startupOptions; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _startupOptions = value;
        }
    }
}

/// <summary>
/// Representation of secret version.
/// </summary>
public class SecretVersion
{
    /// <summary>
    /// Gets or sets secret version ID.
    /// </summary>
    public string? VersionId { get; init; }

    /// <summary>
    /// Gets or sets custom stage name.
    /// </summary>
    public string? VersionStage { get; init; }
}

/// <summary>
/// Options of initial secret fetch.
/// </summary>
public class StartupOptions
{
    /// <summary>
    /// The timeout for the initial fetch of the secret.
    /// </summary>
    /// <remarks>
    /// When provider fails to fetch secret in the given time and secrets source is not optional, exception is thrown.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}