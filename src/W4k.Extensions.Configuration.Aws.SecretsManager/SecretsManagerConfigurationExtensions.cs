﻿using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Extensions for <see cref="IConfigurationBuilder"/> to add AWS Secrets Manager as configuration source.
/// </summary>
public static class SecretsManagerConfigurationExtensions
{
    private const string SecretsManagerClientKey = "W4k:SecretsManagerClient";

    /// <summary>
    /// Gets the default <see cref="IAmazonSecretsManager"/> to be used for AWS Secrets Manager configuration providers.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/>.</param>
    /// <returns>The default <see cref="IAmazonSecretsManager"/> or <see langword="null"/> if not set previously.</returns>
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
    public static IConfigurationBuilder SetSecretsManagerClient(this IConfigurationBuilder builder, IAmazonSecretsManager client)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        builder.Properties[SecretsManagerClientKey] = client;
        return builder;
    }

    /// <summary>
    /// Adds secrets manager configuration source to <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="configureSource">Configures the source.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/>.</returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        Action<SecretsManagerConfigurationSource> configureSource)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureSource);
        return builder.Add(configureSource);
    }

    /*
    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <remarks>
    /// This extension methods uses default <see cref="AmazonSecretsManagerClient"/> instance.
    /// </remarks>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="secretName">Secret name or ID.</param>
    /// <param name="configureOptions">A delegate that is invoked to set up the AWS Secrets Manager Configuration options.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/>.</returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null)
    {
        var client = new AmazonSecretsManagerClient();
        return builder.AddSecretsManager(secretName, client, configureOptions);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="secretName">Secret name or ID.</param>
    /// <param name="client">AWS Secrets Manager client.</param>
    /// <param name="configureOptions">A delegate that is invoked to set up the AWS Secrets Manager Configuration options.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is <see langword="null"/>.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        IAmazonSecretsManager client,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        ArgumentNullException.ThrowIfNull(client);

        var options = new SecretsManagerConfigurationProviderOptions(secretName);
        configureOptions?.Invoke(options);

        return builder.AddSecretsManager(options, client);
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
    /// <returns>Instance of <see cref="IConfigurationBuilder"/>.</returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        IReadOnlyList<string> secretNames,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null)
    {
        var client = new AmazonSecretsManagerClient();
        return builder.AddSecretsManager(secretNames, client, configureOptions);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="secretNames">Collection of secret names to fetch from AWS Secrets Manager.</param>
    /// <param name="client">AWS Secrets Manager client.</param>
    /// <param name="configureOptions">A delegate that is invoked to set up the AWS Secrets Manager Configuration options.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secretNames"/> is empty.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        IReadOnlyList<string> secretNames,
        IAmazonSecretsManager client,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(secretNames);

        if (secretNames.Count == 0)
        {
            ThrowOnEmptySecretNames(secretNames);
        }
        else if (secretNames.Count == 1)
        {
            CreateAndConfigureOptions(builder, secretNames[0]);
        }
        else
        {
            foreach (var secretName in secretNames)
            {
                CreateAndConfigureOptions(builder, secretName);
            }
        }

        return builder;

        void CreateAndConfigureOptions(IConfigurationBuilder cb, string secretName)
        {
            var options = new SecretsManagerConfigurationProviderOptions(secretName);
            configureOptions?.Invoke(options);

            cb.AddSecretsManager(options, client);
        }
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="options">Secrets Manager configuration provider options.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/>.</returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        SecretsManagerConfigurationProviderOptions options)
    {
        var client = new AmazonSecretsManagerClient();
        return builder.AddSecretsManager(options, client);
    }

    /// <summary>
    /// Adds secrets manager as configuration source.
    /// </summary>
    /// <param name="builder">Configuration builder.</param>
    /// <param name="options">Secrets Manager configuration provider options.</param>
    /// <param name="client">AWS Secrets Manager client.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is <see langword="null"/>.</exception>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        SecretsManagerConfigurationProviderOptions options,
        IAmazonSecretsManager client)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);

        return builder.Add(new SecretsManagerConfigurationSource(options, client));
    }
    */

    [DoesNotReturn]
    private static void ThrowOnEmptySecretNames(
        IReadOnlyCollection<string> secretNames,
        [CallerArgumentExpression(nameof(secretNames))] string? paramName = null)
    {
        throw new ArgumentException("At least one secret name must be provided.", paramName);
    }
}