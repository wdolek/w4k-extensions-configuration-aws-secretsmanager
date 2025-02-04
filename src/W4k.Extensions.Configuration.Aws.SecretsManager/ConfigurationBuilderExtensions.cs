using System.Diagnostics.CodeAnalysis;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Extensions for <see cref="IConfigurationBuilder"/> to add AWS Secrets Manager as configuration source.
/// </summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global", Justification = "Shipped as library, anything can be used.")]
public static class ConfigurationBuilderExtensions
{
    private const string SecretsManagerClientKey = "W4k:SecretsManagerClient";

    private static readonly Action<SecretsManagerExceptionContext> OptionalSecretExceptionHandler = context => context.Ignore = true;

    #region Managing shared properties

    /// <summary>
    /// Gets the default <see cref="IAmazonSecretsManager"/> to be used for AWS Secrets Manager configuration providers.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/>.</param>
    /// <returns>The default <see cref="IAmazonSecretsManager"/> or <see langword="null"/> if not set previously.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IAmazonSecretsManager? GetSecretsManagerClient(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Properties.TryGetValue(SecretsManagerClientKey, out var obj) && obj is IAmazonSecretsManager client
            ? client
            : null;
    }

    /// <summary>
    /// Sets the default <see cref="IAmazonSecretsManager"/> to be used for AWS Secrets Manager configuration providers.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="client">The default secrets manager client instance.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is <see langword="null"/>.</exception>
    public static IConfigurationBuilder SetSecretsManagerClient(this IConfigurationBuilder builder, IAmazonSecretsManager client)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        builder.Properties[SecretsManagerClientKey] = client;
        return builder;
    }

    #endregion

    /// <summary>
    /// Adds secrets manager configuration source to <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="configureSource">Configures the source.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configureSource"/> is <see langword="null"/>.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        Action<SecretsManagerConfigurationSource> configureSource)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureSource);
        return builder.Add(configureSource);
    }

    /// <summary>
    /// Adds secrets manager configuration source to <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <param name="configureSource">Configure the source using <see cref="SecretsManagerConfigurationBuilder"/>.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configureSource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        Action<SecretsManagerConfigurationBuilder> configureSource)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        var sourceBuilder = new SecretsManagerConfigurationBuilder(secretName);
        configureSource(sourceBuilder);

        return builder.Add(sourceBuilder.Build());
    }

    /// <summary>
    /// Adds secrets manager configuration source to <paramref name="configurationManager"/>.
    /// </summary>
    /// <param name="configurationManager">The <see cref="IConfigurationManager"/> to add to.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <param name="configureSource">Configure the source using <see cref="SecretsManagerConfigurationBuilder"/>.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationManager"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configureSource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationManager configurationManager,
        string secretName,
        Action<IConfiguration, SecretsManagerConfigurationBuilder> configureSource)
    {
        ArgumentNullException.ThrowIfNull(configurationManager);
        ArgumentNullException.ThrowIfNull(configureSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        var sourceBuilder = new SecretsManagerConfigurationBuilder(secretName);
        configureSource(configurationManager, sourceBuilder);

        return configurationManager.Add(sourceBuilder.Build());
    }

    #region Using default AWS Secrets Manager client

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <remarks>
    /// Uses client set by <see cref="SetSecretsManagerClient"/>, or default instance <see cref="AmazonSecretsManagerClient"/>.
    /// </remarks>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    public static IConfigurationBuilder AddSecretsManager(this IConfigurationBuilder builder, string secretName) =>
        builder.AddSecretsManagerImpl(secretsManager: null!, secretName, configurationKeyPrefix: null, isOptional: false);

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <remarks>
    /// Uses client set by <see cref="SetSecretsManagerClient"/>, or default instance <see cref="AmazonSecretsManagerClient"/>.
    /// </remarks>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <param name="configurationKeyPrefix">Configuration key prefix.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationKeyPrefix"/> is <see langword="null"/>.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        string configurationKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(configurationKeyPrefix);
        return builder.AddSecretsManagerImpl(secretsManager: null!, secretName, configurationKeyPrefix, isOptional: false);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <remarks>
    /// Uses client set by <see cref="SetSecretsManagerClient"/>, or default instance <see cref="AmazonSecretsManagerClient"/>.
    /// </remarks>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <param name="isOptional">Whether secret is optional</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    public static IConfigurationBuilder AddSecretsManager(this IConfigurationBuilder builder, string secretName, bool isOptional) =>
        builder.AddSecretsManagerImpl(secretsManager: null!, secretName, configurationKeyPrefix: null, isOptional);

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <remarks>
    /// Uses client set by <see cref="SetSecretsManagerClient"/>, or default instance <see cref="AmazonSecretsManagerClient"/>.
    /// </remarks>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <param name="configurationKeyPrefix">Configuration key prefix.</param>
    /// <param name="isOptional">Whether secret is optional</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationKeyPrefix"/> is <see langword="null"/>.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        string configurationKeyPrefix,
        bool isOptional)
    {
        ArgumentNullException.ThrowIfNull(configurationKeyPrefix);
        return builder.AddSecretsManagerImpl(secretsManager: null!, secretName, configurationKeyPrefix, isOptional);
    }

    #endregion

    #region Providing AWS Secrets Manager client

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretsManager">The <see cref="IAmazonSecretsManager"/> to use.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretsManager"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        IAmazonSecretsManager secretsManager,
        string secretName)
    {
        ArgumentNullException.ThrowIfNull(secretsManager);
        return builder.AddSecretsManagerImpl(secretsManager, secretName, configurationKeyPrefix: null, isOptional: false);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretsManager">The <see cref="IAmazonSecretsManager"/> to use.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <param name="configurationKeyPrefix">Configuration key prefix.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretsManager"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationKeyPrefix"/> is <see langword="null"/>.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        IAmazonSecretsManager secretsManager,
        string secretName,
        string configurationKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(secretsManager);
        ArgumentNullException.ThrowIfNull(configurationKeyPrefix);
        return builder.AddSecretsManagerImpl(secretsManager, secretName, configurationKeyPrefix, isOptional: false);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretsManager">The <see cref="IAmazonSecretsManager"/> to use.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <param name="isOptional">Whether secret is optional</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretsManager"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        IAmazonSecretsManager secretsManager,
        string secretName,
        bool isOptional)
    {
        ArgumentNullException.ThrowIfNull(secretsManager);
        return builder.AddSecretsManagerImpl(secretsManager, secretName, configurationKeyPrefix: null, isOptional);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="secretsManager">The <see cref="IAmazonSecretsManager"/> to use.</param>
    /// <param name="secretName">Secret name or full secret ARN.</param>
    /// <param name="configurationKeyPrefix">Configuration key prefix.</param>
    /// <param name="isOptional">Whether secret is optional</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretsManager"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretName"/> is <see langword="null"/> or consists only of white-space characters.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationKeyPrefix"/> is <see langword="null"/>.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        IAmazonSecretsManager secretsManager,
        string secretName,
        string configurationKeyPrefix,
        bool isOptional)
    {
        ArgumentNullException.ThrowIfNull(secretsManager);
        ArgumentNullException.ThrowIfNull(configurationKeyPrefix);
        return builder.AddSecretsManagerImpl(secretsManager, secretName, configurationKeyPrefix, isOptional);
    }

    #endregion

    private static IConfigurationBuilder AddSecretsManagerImpl(
        this IConfigurationBuilder builder,
        IAmazonSecretsManager? secretsManager,
        string secretName,
        string? configurationKeyPrefix,
        bool isOptional)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        // NB! Should be safe to pass `null`, builder itself ensures default values are used if not set.
        return builder.AddSecretsManager(
            source =>
            {
                source.SecretsManager = secretsManager!;
                source.SecretName = secretName;
                source.ConfigurationKeyPrefix = configurationKeyPrefix!;

                if (isOptional)
                {
                    source.OnLoadException = OptionalSecretExceptionHandler;
                    source.OnReloadException = OptionalSecretExceptionHandler;
                }
            });
    }
}