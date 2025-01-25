using System.ComponentModel.DataAnnotations;
using Amazon.SecretsManager;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using W4k.Extensions.Configuration.Aws.SecretsManager;
using W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry instrumentation:
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("SecretApi"))
    .WithTracing(
        tracing =>
        {
            tracing.AddAspNetCoreInstrumentation()
                .AddSource(ActivityDescriptors.ActivitySourceName)
                .AddConsoleExporter()
                .AddOtlpExporter();
        });

// AWS Secrets Manager configuration:
var secretsManager = builder.Configuration
    .GetAWSOptions()
    .CreateServiceClient<IAmazonSecretsManager>();

builder.Configuration.AddSecretsManager(
    "w4k/awssm/sample-secret",
    source => source.WithSecretsManager(secretsManager)
        .WithConfigurationKeyPrefix("Secret")
        .WithPollingWatcher(TimeSpan.FromSeconds(60))
        .OnReloadException(ctx => ctx.Ignore = true));

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