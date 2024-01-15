using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal sealed class SecretsManagerConfigurationProvider : ConfigurationProvider
{
    private readonly SecretsFetcher _secretsFetcher;
    private readonly SecretsManagerConfigurationProviderOptions _options;

    public SecretsManagerConfigurationProvider(IAmazonSecretsManager secretsManager, SecretsManagerConfigurationProviderOptions options)
    {
        _secretsFetcher = new SecretsFetcher(secretsManager);
        _options = options;
    }

    public override void Load()
    {
        var secretString = _secretsFetcher.GetSecretString(_options.SecretId, _options.Version).GetAwaiter().GetResult();

        var processor = _options.Processor;
        var data = processor.GetConfigurationData(_options, secretString);

        Data = data;
    }
}