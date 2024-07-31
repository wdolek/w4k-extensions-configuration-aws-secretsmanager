using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal class SecretsManagerConfigurationSource : IConfigurationSource
{
    public SecretsManagerConfigurationSource(
        SecretsManagerConfigurationProviderOptions options,
        IAmazonSecretsManager client,
        bool isOptional)
    {
        Options = options;
        SecretsManager = client;
        IsOptional = isOptional;
    }


    public SecretsManagerConfigurationProviderOptions Options { get; }
    public IAmazonSecretsManager SecretsManager { get; }
    public bool IsOptional { get; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new SecretsManagerConfigurationProvider(this);
}

/// <summary>
/// Extensions for <see cref="IConfigurationBuilder"/> to add AWS Secrets Manager as configuration source.
/// </summary>
public static class SecretsManagerConfigurationExtensions
{
    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <remarks>
    /// This extension methods uses default <see cref="AmazonSecretsManagerClient"/> instance.
    /// </remarks>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="secretName">Secret name or ID.</param>
    /// <param name="configureOptions">A delegate that is invoked to set up the AWS Secrets Manager Configuration options.</param>
    /// <param name="isOptional">Defines the configuration provider's response when a server loading error occurs. If set to false, the error is propagated. If set to true, the error is ignored and no settings are loaded from the AWS Secrets Manager Configuration.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/></returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null,
        bool isOptional = false)
    {
        var client = new AmazonSecretsManagerClient();
        return builder.AddSecretsManager(secretName, client, configureOptions, isOptional);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <remarks>
    /// This extension methods uses default <see cref="AmazonSecretsManagerClient"/> instance.
    /// </remarks>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="secretNames">Collection of secret names to fetch from AWS Secrets Manager.</param>
    /// <param name="configureOptions">A delegate that is invoked to set up the AWS Secrets Manager Configuration options.</param>
    /// <param name="isOptional">Defines the configuration provider's response when a server loading error occurs. If set to false, the error is propagated. If set to true, the error is ignored and no settings are loaded from the AWS Secrets Manager Configuration.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/></returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        IReadOnlyCollection<string> secretNames,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null,
        bool isOptional = false)
    {
        var client = new AmazonSecretsManagerClient();
        return builder.AddSecretsManager(secretNames, client, configureOptions, isOptional);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="secretName">Secret name or ID.</param>
    /// <param name="client">AWS Secrets Manager client.</param>
    /// <param name="configureOptions">A delegate that is invoked to set up the AWS Secrets Manager Configuration options.</param>
    /// <param name="isOptional">Defines the configuration provider's response when a server loading error occurs. If set to false, the error is propagated. If set to true, the error is ignored and no settings are loaded from the AWS Secrets Manager Configuration.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/></returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        IAmazonSecretsManager client,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null,
        bool isOptional = false)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        ArgumentNullException.ThrowIfNull(client);
        
        var providerOptions = new SecretsManagerConfigurationProviderOptions(secretName);
        configureOptions?.Invoke(providerOptions);

        return builder.Add(new SecretsManagerConfigurationSource(providerOptions, client, isOptional));
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="secretNames">Collection of secret names to fetch from AWS Secrets Manager.</param>
    /// <param name="client">AWS Secrets Manager client.</param>
    /// <param name="configureOptions">A delegate that is invoked to set up the AWS Secrets Manager Configuration options.</param>
    /// <param name="isOptional">Defines the configuration provider's response when a server loading error occurs. If set to false, the error is propagated. If set to true, the error is ignored and no settings are loaded from the AWS Secrets Manager Configuration.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/></returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        IReadOnlyCollection<string> secretNames,
        IAmazonSecretsManager client,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null,
        bool isOptional = false)
    {
        ArgumentNullException.ThrowIfNull(secretNames);
        ArgumentNullException.ThrowIfNull(client);

        if (secretNames.Count == 0)
        {
            ThrowOnEmptySecretNames(secretNames);
        }

        foreach (var secretName in secretNames)
        {
            var providerOptions = new SecretsManagerConfigurationProviderOptions(secretName);
            configureOptions?.Invoke(providerOptions);

            builder.Add(new SecretsManagerConfigurationSource(providerOptions, client, isOptional));
        }

        return builder;
    }

    [DoesNotReturn]
    private static void ThrowOnEmptySecretNames(
        IReadOnlyCollection<string> secretNames,
        [CallerArgumentExpression("secretNames")] string? paramName = null)
    {
        throw new ArgumentException("At least one secret name must be provided.", paramName);
    }
}