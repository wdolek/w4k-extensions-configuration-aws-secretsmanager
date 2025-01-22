using System.Diagnostics;

namespace W4k.Extensions.Configuration.Aws.SecretsManager.Diagnostics;

#if !NET9_0_OR_GREATER
internal static class ExceptionExtensions
{
    public static ActivityEvent ToActivityEvent(this Exception exception)
    {
        // following OpenTelemetry conventions: https://opentelemetry.io/docs/specs/otel/trace/exceptions/
        const string exceptionEventName = "exception";
        const string exceptionMessageTag = "exception.message";
        const string exceptionTypeTag = "exception.type";

        return new ActivityEvent(
            exceptionEventName,
            tags: new ActivityTagsCollection
            {
                [exceptionMessageTag] = exception.Message,
                [exceptionTypeTag] = exception.GetType().FullName,
            });
    }
}
#endif