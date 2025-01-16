using System.Diagnostics.CodeAnalysis;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// AWS Secrets Manager configuration source.
/// </summary>
[SuppressMessage("ReSharper", "RedundantNullableFlowAttribute", Justification = "Consumers of library may not have nullable reference types enabled.")]
public class SecretsManagerConfigurationSource : IConfigurationSource
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(24);

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
    /// Gets or sets flag indicating whether secret is optional.
    /// </summary>
    public bool IsOptional { get; set; }

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
    /// Gets or sets action to be executed when exception occurs during refreshing secret.
    /// </summary>
    public Action<SecretsManagerExceptionContext>? OnRefreshException { get; set; }

    /// <summary>
    /// Gets or sets logger factory.
    /// </summary>
    /// <remarks>
    /// By default, <see cref="NullLoggerFactory"/> is used.
    /// </remarks>
    [DisallowNull]
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="SecretName"/> is not set.</exception>
    [SuppressMessage("ReSharper", "NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract", Justification = "Consumers of library may not have nullable reference types enabled.")]
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(SecretName))
        {
            throw new InvalidOperationException("Secret name must be provided.");
        }

        // ensure default values
        SecretsManager ??= builder.GetSecretsManagerClient() ?? new AmazonSecretsManagerClient();
        ConfigurationKeyPrefix ??= String.Empty;
        Processor ??= SecretsProcessor.Json;
        LoggerFactory ??= NullLoggerFactory.Instance;

        return new SecretsManagerConfigurationProvider(this);
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