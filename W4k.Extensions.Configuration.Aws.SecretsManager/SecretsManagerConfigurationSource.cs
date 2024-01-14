using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal class SecretsManagerConfigurationSource : IConfigurationSource
{
    private readonly SecretsManagerConfigurationProviderOptions _options;
    private readonly IAmazonSecretsManager _client;

    public SecretsManagerConfigurationSource(SecretsManagerConfigurationProviderOptions options, IAmazonSecretsManager client)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        _options = options;
        _client = client;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SecretsManagerConfigurationProvider(_client, _options);
    }
}

public static class SecretsManagerConfigurationExtensions
{
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null)
    {
        var client = new AmazonSecretsManagerClient();
        return builder.AddSecretsManager(secretName, client, configureOptions);
    }

    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder builder,
        string secretName,
        IAmazonSecretsManager client,
        Action<SecretsManagerConfigurationProviderOptions>? configureOptions = null)
    {
        var providerOptions = new SecretsManagerConfigurationProviderOptions { SecretId = secretName };
        configureOptions?.Invoke(providerOptions);

        return builder.Add(new SecretsManagerConfigurationSource(providerOptions, client));
    }
}