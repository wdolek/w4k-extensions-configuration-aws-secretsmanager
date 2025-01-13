using System.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

internal static class ExceptionExtensions
{
    public static ActivityEvent ToActivityEvent(this Exception e, string eventName)
    {
        return new ActivityEvent(
            eventName,
            tags: new ActivityTagsCollection
            {
                ["exception.type"] = e.GetType().FullName,
                ["exception.message"] = e.Message,
            });
    }
}