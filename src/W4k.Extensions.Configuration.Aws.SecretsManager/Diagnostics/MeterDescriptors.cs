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
        description: "Initial loads attempted");

    internal static Counter<long> Reloads { get; } = Meter.CreateCounter<long>(
        "w4k.secretsmanager.reloads",
        description: "Reloads that changed configuration data");

    internal static Counter<long> ReloadsSkipped { get; } = Meter.CreateCounter<long>(
        "w4k.secretsmanager.reloads.skipped",
        description: "Reloads where the secret version was unchanged");

    internal static Counter<long> Failures { get; } = Meter.CreateCounter<long>(
        "w4k.secretsmanager.failures",
        description: "Load or reload failures, tagged by phase");

    internal static Histogram<double> FetchDuration { get; } = Meter.CreateHistogram<double>(
        "w4k.secretsmanager.fetch.duration",
        unit: "s",
        description: "Fetch wall time in seconds");
}
