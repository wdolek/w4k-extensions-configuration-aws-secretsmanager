using System.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

internal static class ExceptionExtensions
{
    public static ActivityEvent ToActivityEvent(this Exception e)
    {
        // following OpenTelemetry conventions: https://opentelemetry.io/docs/specs/otel/trace/exceptions/
        const string exceptionEventName = "exception";
        const string exceptionMessageTag = "exception.message";
        const string exceptionTypeTag = "exception.type";

        return new ActivityEvent(
            exceptionEventName,
            tags: new ActivityTagsCollection
            {
                [exceptionMessageTag] = e.Message,
                [exceptionTypeTag] = e.GetType().FullName,
            });
    }
}