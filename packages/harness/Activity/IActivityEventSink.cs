namespace PersonalAssistant.Harness.Activity;

public interface IActivityEventSink
{
    void Append(ActivityEvent activityEvent);
}
