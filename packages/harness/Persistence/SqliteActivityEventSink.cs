using PersonalAssistant.Harness.Activity;

namespace PersonalAssistant.Harness.Persistence;

public sealed class SqliteActivityEventSink(SqliteHarnessDatabase database) : IActivityEventSink
{
    public void Append(ActivityEvent activityEvent)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);
        database.ExecuteInTransaction(transaction => database.InsertActivityEvent(transaction, activityEvent));
    }
}
