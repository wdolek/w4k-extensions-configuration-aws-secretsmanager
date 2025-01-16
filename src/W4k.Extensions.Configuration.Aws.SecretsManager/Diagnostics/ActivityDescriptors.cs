using System.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

/// <summary>
/// Activity descriptors for AWS Secrets Manager configuration provider.
/// </summary>
public static class ActivityDescriptors
{
    /// <summary>
    /// Activity source name.
    /// </summary>
    public static readonly string ActivitySourceName = "W4k.Extensions.Configuration.Aws.SecretsManager";

    /// <summary>
    /// Name of activity representing load of secrets from AWS Secrets Manager.
    /// </summary>
    public static readonly string LoadActivityName = "Load";

    /// <summary>
    /// Name of activity representing consecutive load of secrets from AWS Secrets Manager.
    /// </summary>
    public static readonly string ReloadActivityName = "Reload";

    internal static ActivitySource Source { get; } = new(ActivitySourceName);
}