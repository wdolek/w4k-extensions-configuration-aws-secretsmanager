﻿using System.Diagnostics;

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

    internal static ActivitySource Source { get; } = new(ActivitySourceName, "2.1");
}