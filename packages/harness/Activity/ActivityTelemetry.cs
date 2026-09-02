namespace PersonalAssistant.Harness.Activity;

public static class ActivityTelemetry
{
    public static void TryRecord(IActivityEventSink sink, ActivityEvent activityEvent)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(activityEvent);

        try
        {
            sink.Append(activityEvent);
        }
        catch
        {
            // Activity telemetry must never break runtime request paths.
        }
    }
}
