using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Configuration of <see cref="SecretsManagerConfigurationProvider"/>.
/// </summary>
public sealed class SecretsManagerConfigurationProviderOptions
{
    private string _configKeyPrefix = "";
    private ISecretProcessor _secretProcessor = SecretsProcessor.Json;
    private StartupOptions _startupOptions = new();
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    /// <summary>
    /// Initializes new instance of <see cref="SecretsManagerConfigurationProviderOptions"/>.
    /// </summary>
    /// <param name="secretName">Secret name (or its complete ARN) to fetch.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretName"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationProviderOptions(string secretName)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        SecretName = secretName;
    }

    /// <summary>
    /// Gets secret name (or its complete ARN) to fetch.
    /// </summary>
    public string SecretName { get; }

    /// <summary>
    /// Gets or sets a value indicating whether secret is mandatory (throws when failed to load) or optional (silently ignores).
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>
    /// Gets or sets secret version to fetch, if not provided, latest version of secret is fetched.
    /// </summary>
    public SecretVersion? Version { get; set; }

    /// <summary>
    /// Gets or sets configuration key prefix, if not set, secret properties are placed directly in configuration root.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when value is <see langword="null"/>.</exception>
    public string ConfigurationKeyPrefix
    {
        get { return _configKeyPrefix; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _configKeyPrefix = value;
        }
    }

    /// <summary>
    /// Gets or sets secrets processor (parsing, tokenizing), default is <see cref="SecretsProcessor.Json"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when value is <see langword="null"/>.</exception>
    public ISecretProcessor Processor
    {
        get { return _secretProcessor; }
        set
        {
            ArgumentNullException.ThrowIfNull(_secretProcessor);
            _secretProcessor = value;
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
    /// <exception cref="ArgumentNullException">Thrown when value is <see langword="null"/>.</exception>
    public StartupOptions Startup
    {
        get { return _startupOptions; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _startupOptions = value;
        }
    }

    /// <summary>
    /// Gets or sets logger factory.
    /// </summary>
    /// <remarks>
    /// By default, <see cref="NullLoggerFactory"/> is used.
    /// </remarks>
    public ILoggerFactory LoggerFactory
    {
        get { return _loggerFactory; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _loggerFactory = value;
        }
    }
}

/// <summary>
/// Representation of secret version.
/// </summary>
public sealed class SecretVersion
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
public sealed class StartupOptions
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The timeout for the initial fetch of the secret, default is 60 seconds.
    /// </summary>
    /// <remarks>
    /// When provider fails to fetch secret in the given time and configuration source is not optional, exception is thrown.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public TimeSpan Timeout
    {
        get { return _timeout; }
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _timeout = value;
        }
    }
}