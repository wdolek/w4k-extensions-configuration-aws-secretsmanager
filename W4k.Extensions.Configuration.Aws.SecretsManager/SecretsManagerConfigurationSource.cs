using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal class SecretsManagerConfigurationSource : IConfigurationSource
{
    private readonly SecretsManagerConfigurationProviderOptions _options;
    private readonly IAmazonSecretsManager _client;

    public SecretsManagerConfigurationSource(SecretsManagerConfigurationProviderOptions options, IAmazonSecretsManager client)
    {
        _options = options;
        _client = client;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SecretsManagerConfigurationProvider(_client, _options, isOptional: false);
    }
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
    /// <param name="configureOptions">Configure options callback.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/></returns>
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
    /// <param name="configureOptions">Configure options callback.</param>
    /// <returns>Instance of <see cref="IConfigurationBuilder"/></returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        IAmazonSecretsManager client,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null)
    {
#if !NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(secretName);
#endif
        var providerOptions = new SecretsManagerConfigurationProviderOptions { SecretName = secretName };
        configureOptions?.Invoke(providerOptions);

        return builder.Add(new SecretsManagerConfigurationSource(providerOptions, client));
    }
}