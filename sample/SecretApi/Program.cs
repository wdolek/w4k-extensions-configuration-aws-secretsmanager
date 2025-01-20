using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Amazon.SecretsManager;
using Microsoft.Extensions.Options;
using W4k.Extensions.Configuration.Aws.SecretsManager;
using W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

var listener = new ActivityListener().ListenToSecretsManagerActivitySource(
    onStart => Console.WriteLine($"[{onStart.StartTimeUtc:O}] {onStart.Source.Name}:{onStart.OperationName} Started"),
    onStop => Console.WriteLine($"[{onStop.StartTimeUtc:O}] {onStop.Source.Name}:{onStop.OperationName} Stopped"));

ActivitySource.AddActivityListener(listener);

var secretsManager = builder.Configuration
    .GetAWSOptions()
    .CreateServiceClient<IAmazonSecretsManager>();

builder.Configuration.AddSecretsManager(
    src =>
    {
        src.SecretsManager = secretsManager;
        src.SecretName = "w4k/awssm/sample-secret";
        src.ConfigurationKeyPrefix = "Secret";
        src.ConfigurationWatcher = new SecretsManagerPollingWatcher(TimeSpan.FromSeconds(60));
        src.Timeout = TimeSpan.FromSeconds(10);
    });

builder.Services.AddOptions<SampleOptions>()
    .BindConfiguration("Secret")
    .ValidateDataAnnotations()
    .ValidateOnStart();

var app = builder.Build();

app.MapGet("/secret", (IOptionsSnapshot<SampleOptions> options) => Results.Ok(options.Value)).WithName("GetSecret");

app.Run();

class SampleOptions
{
    [Required]
    public required string ClientId { get; init; }

    [Required]
    public required string ClientSecret { get; init; }
}