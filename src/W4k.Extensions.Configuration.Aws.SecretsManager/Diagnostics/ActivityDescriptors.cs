using System.Diagnostics;
using System.Reflection;

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
    public static readonly string LoadActivityName = "W4k.SecretsManager.Load";

    /// <summary>
    /// Name of activity representing reload of secrets from AWS Secrets Manager.
    /// </summary>
    public static readonly string ReloadActivityName = "W4k.SecretsManager.Reload";

    internal static ActivitySource Source { get; } = new(ActivitySourceName, GetVersion());

    // derives the version from the assembly informational version so it tracks the package
    // version automatically; the SourceLink commit suffix (`2.3.0+abc1234`) is trimmed to
    // match the package version exactly
    private static string? GetVersion()
    {
        var informationalVersion = typeof(ActivityDescriptors).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return informationalVersion?.Split('+')[0];
    }
}