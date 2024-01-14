using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretsManagerConfigurationProvider(
    IAmazonSecretsManager secretsManager,
    SecretsManagerConfigurationProviderOptions options)
    : ConfigurationProvider
{
    private readonly SecretsFetcher _secretsFetcher = new(secretsManager);
    private readonly SecretsManagerConfigurationProviderOptions _options = options;

    public override void Load()
    {
        var secretString = _secretsFetcher.GetSecretString(_options.SecretId, _options.Version).GetAwaiter().GetResult();

        var processor = _options.Processor;
        var data = processor.GetConfigurationData(_options, secretString);

        Data = data;
    }
}