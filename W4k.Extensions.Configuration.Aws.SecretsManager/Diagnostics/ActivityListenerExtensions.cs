using System.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

/// <summary>
/// Extensions for <see cref="ActivityListener"/>.
/// </summary>
public static class ActivityListenerExtensions
{
    /// <summary>
    /// Configure <see cref="ActivityListener"/> to listen to <see cref="ActivityDescriptors.ActivitySourceName"/> activity source.
    /// </summary>
    /// <param name="listener">Activity listener which should listen to <see cref="ActivityDescriptors.ActivitySourceName"/> activity source.</param>
    /// <returns>Configured activity listener.</returns>
    public static ActivityListener ListenToSecretsManagerActivitySource(this ActivityListener listener)
    {
        listener.ShouldListenTo = activitySource => activitySource.Name == ActivityDescriptors.ActivitySourceName;
        return listener;
    }
}