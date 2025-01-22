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
    /// <param name="listener">Activity listener which should listen to &quot;<see cref="ActivityDescriptors.ActivitySourceName"/>&quot; activity source.</param>
    /// <param name="onStart">Callback invoked when activity is started.</param>
    /// <param name="onStop">Callback invoked when activity is stopped.</param>
    /// <returns>Configured activity listener.</returns>
    public static ActivityListener ListenToSecretsManagerActivitySource(
        this ActivityListener listener,
        Action<Activity> onStart,
        Action<Activity> onStop)
    {
        listener.ActivityStarted = onStart;
        listener.ActivityStopped = onStop;
        listener.ShouldListenTo = activitySource => activitySource.Name == ActivityDescriptors.ActivitySourceName;
        listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData;

        return listener;
    }
}