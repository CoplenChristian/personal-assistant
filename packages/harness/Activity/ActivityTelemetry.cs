namespace PersonalAssistant.Harness.Activity;

/// <summary>
/// Best-effort activity recording that never breaks runtime request paths.
/// When recording fails, <see cref="RecordingDegraded"/> is set so read APIs can
/// surface a degraded-audit signal without blocking terminal input delivery.
/// </summary>
public static class ActivityTelemetry
{
    private static int failedRecordCount;

    public static bool RecordingDegraded => Volatile.Read(ref failedRecordCount) > 0;

    public static int FailedRecordCount => Volatile.Read(ref failedRecordCount);

    public static void ResetForTests()
    {
        Volatile.Write(ref failedRecordCount, 0);
    }

    public static void RecordFailure()
    {
        Interlocked.Increment(ref failedRecordCount);
    }

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
            // SQLite-backed sinks record failures in InsertActivityEvent; swallow so
            // terminal input and other request paths stay available.
        }
    }
}
