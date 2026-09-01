using PersonalAssistant.Harness.Activity;

namespace PersonalAssistant.Harness.Persistence;

public interface ISettingsOverrideStore : IDisposable
{
    IReadOnlyDictionary<string, string> ReadGlobalOverrides();

    void ApplyAtomic(IReadOnlyDictionary<string, string?> changes, ActivityEvent? activityEvent);

    IReadOnlyList<ActivityEvent> ReadActivityEvents();
}
