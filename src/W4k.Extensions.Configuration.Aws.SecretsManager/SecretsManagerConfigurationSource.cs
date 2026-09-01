using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// AWS Secrets Manager configuration source.
/// </summary>
[SuppressMessage("ReSharper", "RedundantNullableFlowAttribute", Justification = "Consumers of library may not have nullable reference types enabled.")]
public sealed class SecretsManagerConfigurationSource : IConfigurationSource
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private bool _built;

    /// <summary>
    /// Gets or sets secret name (or its complete ARN) to fetch.
    /// </summary>
    [DisallowNull]
    public string SecretName { get; set; } = null!;

    /// <summary>
    /// Gets or sets AWS Secrets Manager client.
    /// </summary>
    [DisallowNull]
    public IAmazonSecretsManager SecretsManager { get; set; } = null!;

    /// <summary>
    /// Gets or sets secret version to fetch, if not provided, latest version of secret is fetched.
    /// </summary>
    public SecretVersion? Version { get; set; }

    /// <summary>
    /// Gets or sets secrets processor (parsing, tokenizing), default is <see cref="SecretsProcessor.Json"/>.
    /// </summary>
    [DisallowNull]
    public ISecretProcessor Processor { get; set; } = SecretsProcessor.Json;

    /// <summary>
    /// Gets list of configuration key transformers applied after tokenization.
    /// </summary>
    /// <remarks>
    /// Transformers are applied in the order they are added to the list.
    /// Result of previous transformer is input for the next one.
    /// By default, only <see cref="KeyDelimiterTransformer"/> is present.
    /// </remarks>
    public List<IConfigurationKeyTransformer> KeyTransformers { get; } = [new KeyDelimiterTransformer()];

    /// <summary>
    /// Gets snapshot of <see cref="KeyTransformers"/> taken when the source is built, <see langword="null"/> when the source has not been built yet.
    /// </summary>
    internal IConfigurationKeyTransformer[]? KeyTransformersSnapshot { get; private set; }

    /// <summary>
    /// Gets key transformers effective for secret processing: the snapshot taken when the source is built
    /// (see <see cref="KeyTransformersSnapshot"/>), or the live <see cref="KeyTransformers"/> collection
    /// when the source has not been built yet.
    /// </summary>
    /// <returns>Span of key transformers to apply.</returns>
    internal ReadOnlySpan<IConfigurationKeyTransformer> GetKeyTransformers() =>
        KeyTransformersSnapshot is { } snapshot
            ? snapshot
            : CollectionsMarshal.AsSpan(KeyTransformers);

    /// <summary>
    /// Gets or sets configuration key prefix, if not set, secret properties are placed directly in configuration root.
    /// </summary>
    [DisallowNull]
    public string ConfigurationKeyPrefix { get; set; } = "";

    /// <summary>
    /// Gets or sets configuration change watcher.
    /// </summary>
    public IConfigurationWatcher? ConfigurationWatcher { get; set; }

    /// <summary>
    /// The timeout for the secret fetch operation.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _timeout = value;
        }
    }

    /// <summary>
    /// Gets or sets action to be executed when exception occurs during loading secret.
    /// </summary>
    public Action<SecretsManagerExceptionContext>? OnLoadException { get; set; }

    /// <summary>
    /// Gets or sets action to be executed when exception occurs during reloading secret.
    /// </summary>
    public Action<SecretsManagerExceptionContext>? OnReloadException { get; set; }

    /// <summary>
    /// Gets or sets logger factory.
    /// </summary>
    /// <remarks>
    /// By default, <see cref="NullLoggerFactory"/> is used.
    /// </remarks>
    [DisallowNull]
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="SecretName"/> is not set, or when the source has already been built.</exception>
    [SuppressMessage("ReSharper", "NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract", Justification = "Consumers of library may not have nullable reference types enabled.")]
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(SecretName))
        {
            throw new InvalidOperationException("Secret name must be provided.");
        }

        if (_built)
        {
            throw new InvalidOperationException("Configuration source has already been built; create a new source per builder.");
        }

        _built = true;

        // ensure default values
        SecretsManager ??= builder.GetSecretsManagerClient() ?? new AmazonSecretsManagerClient();
        ConfigurationKeyPrefix ??= String.Empty;
        Processor ??= SecretsProcessor.Json;
        LoggerFactory ??= NullLoggerFactory.Instance;

        // snapshot key transformers; mutating the list after the source is built has no effect
        KeyTransformersSnapshot = KeyTransformers.ToArray();

        return new SecretsManagerConfigurationProvider(this);
    }
}

/// <summary>
/// Builder for <see cref="SecretsManagerConfigurationSource"/>.
/// </summary>
public sealed class SecretsManagerConfigurationBuilder
{
    private readonly SecretsManagerConfigurationSource _source;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerConfigurationBuilder"/> class.
    /// </summary>
    /// <param name="secretName">Secret name (or its complete ARN) to fetch.</param>
    public SecretsManagerConfigurationBuilder(string secretName)
    {
        _source = new SecretsManagerConfigurationSource { SecretName = secretName };
    }

    /// <summary>
    /// Sets <see cref="IAmazonSecretsManager"/> to the configuration source.
    /// </summary>
    /// <param name="secretsManager">AWS Secrets Manager client.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretsManager"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationBuilder WithSecretsManager(IAmazonSecretsManager secretsManager)
    {
        ArgumentNullException.ThrowIfNull(secretsManager);
        _source.SecretsManager = secretsManager;

        return this;
    }

    /// <summary>
    /// Sets secret version to fetch.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> is provided, latest version of secret is fetched.
    /// </remarks>
    /// <param name="versionId">Version ID.</param>
    /// <param name="versionStage">Version stage name.</param>
    /// <returns>Current builder instance.</returns>
    public SecretsManagerConfigurationBuilder WithVersion(string? versionId = null, string? versionStage = null)
    {
        _source.Version = new SecretVersion
        {
            VersionId = versionId,
            VersionStage = versionStage,
        };

        return this;
    }

    /// <summary>
    /// Sets custom <see cref="ISecretProcessor"/> as secret processor.
    /// </summary>
    /// <param name="processor">Secret processor.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="processor"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationBuilder WithProcessor(ISecretProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _source.Processor = processor;

        return this;
    }

    /// <summary>
    /// Sets <see cref="SecretsProcessor.Json"/> as secret processor.
    /// </summary>
    /// <remarks>
    /// <see cref="SecretsProcessor.Json"/> is used by default. Use this method to explicitly set it or overwrite another processor set.
    /// </remarks>
    /// <returns>Current builder instance.</returns>
    public SecretsManagerConfigurationBuilder WithJsonProcessor()
    {
        _source.Processor = SecretsProcessor.Json;
        return this;
    }

    /// <summary>
    /// Sets <see cref="SecretsProcessor.PlainText"/> as secret processor.
    /// </summary>
    /// <remarks>
    /// The secret value is placed under <see cref="SecretsManagerConfigurationSource.ConfigurationKeyPrefix"/> verbatim.
    /// </remarks>
    /// <returns>Current builder instance.</returns>
    public SecretsManagerConfigurationBuilder WithPlainTextProcessor()
    {
        _source.Processor = SecretsProcessor.PlainText;
        return this;
    }

    /// <summary>
    /// Sets <see cref="PlainTextSecretProcessor"/> with explicit configuration key as secret processor.
    /// </summary>
    /// <remarks>
    /// The secret value is placed under <c>{ConfigurationKeyPrefix}:{configurationKey}</c>,
    /// or under the key standalone when no prefix is set.
    /// </remarks>
    /// <param name="configurationKey">Configuration key the secret value is placed under.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configurationKey"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    public SecretsManagerConfigurationBuilder WithPlainTextProcessor(string configurationKey)
    {
        _source.Processor = new PlainTextSecretProcessor(configurationKey);
        return this;
    }

    /// <summary>
    /// Clears all key transformers.
    /// </summary>
    /// <returns>Current builder instance.</returns>
    public SecretsManagerConfigurationBuilder ClearKeyTransformers()
    {
        _source.KeyTransformers.Clear();
        return this;
    }

    /// <summary>
    /// Adds <see cref="IConfigurationKeyTransformer"/> to collection of key transformers.
    /// </summary>
    /// <remarks>
    /// Transformers are applied in the order they are added to the list.
    /// </remarks>
    /// <param name="transformer">Key transformer.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transformer"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationBuilder AddKeyTransformer(IConfigurationKeyTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        _source.KeyTransformers.Add(transformer);

        return this;
    }

    /// <summary>
    /// Sets configuration key prefix.
    /// </summary>
    /// <param name="configurationKeyPrefix">Configuration key prefix.</param>
    /// <returns>Current builder instance.</returns>
    public SecretsManagerConfigurationBuilder WithConfigurationKeyPrefix(string configurationKeyPrefix)
    {
        _source.ConfigurationKeyPrefix = configurationKeyPrefix;
        return this;
    }

    /// <summary>
    /// Sets <see cref="IConfigurationWatcher"/> to watch for configuration changes.
    /// </summary>
    /// <param name="configurationWatcher">Configuration watcher.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationWatcher"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationBuilder WithConfigurationWatcher(IConfigurationWatcher configurationWatcher)
    {
        ArgumentNullException.ThrowIfNull(configurationWatcher);
        _source.ConfigurationWatcher = configurationWatcher;

        return this;
    }

    /// <summary>
    /// Sets <see cref="SecretsManagerPollingWatcher"/> to periodically watch for configuration changes.
    /// </summary>
    /// <param name="pollingInterval">Polling interval.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pollingInterval"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public SecretsManagerConfigurationBuilder WithPollingWatcher(TimeSpan pollingInterval)
    {
        _source.ConfigurationWatcher = new SecretsManagerPollingWatcher(pollingInterval);
        return this;
    }

    /// <summary>
    /// Sets <see cref="SecretsManagerPollingWatcher"/> to periodically watch for configuration changes.
    /// </summary>
    /// <param name="pollingInterval">Polling interval.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pollingInterval"/> is less or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationBuilder WithPollingWatcher(TimeSpan pollingInterval, TimeProvider timeProvider)
    {
        _source.ConfigurationWatcher = new SecretsManagerPollingWatcher(pollingInterval, timeProvider);
        return this;
    }

    /// <summary>
    /// Sets timeout for the secret fetch operation.
    /// </summary>
    /// <param name="timeout">Timeout.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public SecretsManagerConfigurationBuilder WithTimeout(TimeSpan timeout)
    {
        _source.Timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets error handler for exception thrown during loading secret.
    /// </summary>
    /// <param name="action">On load error callback.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationBuilder OnLoadException(Action<SecretsManagerExceptionContext> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _source.OnLoadException = action;

        return this;
    }

    /// <summary>
    /// Sets error handler for exception thrown during reloading secret.
    /// </summary>
    /// <param name="action">On reload error callback.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationBuilder OnReloadException(Action<SecretsManagerExceptionContext> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _source.OnReloadException = action;

        return this;
    }

    /// <summary>
    /// Sets <see cref="ILoggerFactory"/>.
    /// </summary>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Current builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="loggerFactory"/> is <see langword="null"/>.</exception>
    public SecretsManagerConfigurationBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _source.LoggerFactory = loggerFactory;

        return this;
    }

    /// <summary>
    /// Builds <see cref="SecretsManagerConfigurationProvider"/>.
    /// </summary>
    /// <returns>Configuration source.</returns>
    public SecretsManagerConfigurationSource Build()
    {
        return _source;
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