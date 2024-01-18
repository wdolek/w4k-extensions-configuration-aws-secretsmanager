using System.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager;

internal static class Activity
{
    public static ActivitySource Source { get; } = new("W4k.Extensions.Configuration.Aws.SecretsManager");
}