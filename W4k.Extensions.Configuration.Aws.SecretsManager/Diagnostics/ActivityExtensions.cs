using System.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

/// <summary>
/// Extensions for <see cref="Activity"/>.
/// </summary>
public static class ActivityExtensions
{
    /// <summary>
    /// Helper method to format activity start.
    /// </summary>
    /// <param name="activity">Activity to format.</param>
    /// <returns>Formatted activity start.</returns>
    public static string FormatStartActivity(this Activity activity) =>
        $"[{activity.StartTimeUtc:O}] {activity.Source.Name}:{activity.OperationName} Started";
    
    /// <summary>
    /// Helper method to format activity stop.
    /// </summary>
    /// <param name="activity">Activity to format.</param>
    /// <returns>Formatted activity stop.</returns>
    public static string FormatStopActivity(this Activity activity)
    {
        var evt = activity.Events.FirstOrDefault();
        var hasEvent = !string.IsNullOrEmpty(evt.Name);

        var err = activity.Tags.FirstOrDefault(t => t.Key == "Error");
        var hasError = !string.IsNullOrEmpty(err.Key);

        return (hasEvent, hasError) switch
        {
            (true, true) => $"[{activity.StartTimeUtc:O}] {activity.Source.Name}:{activity.OperationName} Stopped, duration: {activity.Duration}; {evt.Name}; Failed: {err.Value}",
            (true, false) => $"[{activity.StartTimeUtc:O}] {activity.Source.Name}:{activity.OperationName} Stopped, duration: {activity.Duration}; {evt.Name}",
            _ => $"[{activity.StartTimeUtc:O}] {activity.Source.Name}:{activity.OperationName} Stopped, duration: {activity.Duration}"
        };
    }
}