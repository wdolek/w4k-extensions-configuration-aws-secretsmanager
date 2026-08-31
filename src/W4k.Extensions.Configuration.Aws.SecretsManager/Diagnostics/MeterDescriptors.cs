using System.Diagnostics.Metrics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

/// <summary>
/// Meter descriptors for AWS Secrets Manager configuration provider.
/// </summary>
public static class MeterDescriptors
{
    /// <summary>
    /// Meter name.
    /// </summary>
    public static readonly string MeterName = "W4k.Extensions.Configuration.Aws.SecretsManager";

    // version derived from the assembly informational version, same as the activity source,
    // so both track the package version automatically
    internal static Meter Meter { get; } = new(MeterName, ActivityDescriptors.Source.Version);

    internal static Counter<long> Loads { get; } = Meter.CreateCounter<long>(
        "w4k.secretsmanager.loads",
        unit: "{operation}",
        description: "Initial loads attempted");

    internal static Counter<long> Reloads { get; } = Meter.CreateCounter<long>(
        "w4k.secretsmanager.reloads",
        unit: "{operation}",
        description: "Reloads that changed configuration data");

    internal static Counter<long> ReloadsSkipped { get; } = Meter.CreateCounter<long>(
        "w4k.secretsmanager.reloads.skipped",
        unit: "{operation}",
        description: "Reloads where the secret version was unchanged");

    internal static Counter<long> LoadFailures { get; } = Meter.CreateCounter<long>(
        "w4k.secretsmanager.loads.failed",
        unit: "{operation}",
        description: "Initial loads that failed");

    internal static Counter<long> ReloadFailures { get; } = Meter.CreateCounter<long>(
        "w4k.secretsmanager.reloads.failed",
        unit: "{operation}",
        description: "Reloads that failed");
}