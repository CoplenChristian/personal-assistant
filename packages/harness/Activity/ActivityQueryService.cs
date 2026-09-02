using System.Globalization;
using PersonalAssistant.Harness.Persistence;

namespace PersonalAssistant.Harness.Activity;

public sealed class ActivityQueryService
{
    public const string ContractVersion = "phase-0c-activity.v1";
    public const int DefaultFeedLimit = 50;
    public const int MaxFeedLimit = 100;

    private readonly SqliteHarnessDatabase database;

    public ActivityQueryService(SqliteHarnessDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public ActivityQueryResult Query(ActivityQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var timezone = ResolveTimezone(request.TimezoneId);
        var localDate = ParseDate(request.Date, timezone);
        var (startUtc, endUtc) = GetDayBounds(localDate, timezone);
        var feedLimit = ClampFeedLimit(request.FeedLimit);
        var events = database.ReadActivityEventsBetween(startUtc, endUtc);
        var counters = ActivityCounterAggregator.Aggregate(events);
        var recentEvents = events
            .OrderByDescending(activityEvent => activityEvent.Timestamp)
            .ThenByDescending(activityEvent => activityEvent.Id, StringComparer.Ordinal)
            .Take(feedLimit)
            .Select(ActivityRedaction.ToPublicEvent)
            .ToArray();

        return new ActivityQueryResult(
            ContractVersion,
            localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            timezone.Id,
            counters,
            recentEvents,
            feedLimit);
    }

    private static TimeZoneInfo ResolveTimezone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId) || string.Equals(timezoneId, "local", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ActivityQueryException("activity_timezone_invalid", "The requested activity timezone is not recognized.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ActivityQueryException("activity_timezone_invalid", "The requested activity timezone is invalid.");
        }
    }

    private static DateOnly ParseDate(string? date, TimeZoneInfo timezone)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone);
            return DateOnly.FromDateTime(nowLocal.DateTime);
        }

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new ActivityQueryException("activity_date_invalid", "The requested activity date must use YYYY-MM-DD.");
        }

        return parsed;
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetDayBounds(DateOnly localDate, TimeZoneInfo timezone)
    {
        var startLocal = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddDays(1);
        var startUtc = new DateTimeOffset(startLocal, timezone.GetUtcOffset(startLocal)).ToUniversalTime();
        var endUtc = new DateTimeOffset(endLocal, timezone.GetUtcOffset(endLocal)).ToUniversalTime();
        return (startUtc, endUtc);
    }

    private static int ClampFeedLimit(int? feedLimit)
    {
        if (feedLimit is null)
        {
            return DefaultFeedLimit;
        }

        if (feedLimit is < 1 or > MaxFeedLimit)
        {
            throw new ActivityQueryException("activity_feed_limit_invalid", "The activity feed limit must be between 1 and 100.");
        }

        return feedLimit.Value;
    }
}

public sealed record ActivityQueryRequest(string? Date, string? TimezoneId, int? FeedLimit);

public sealed record ActivityQueryResult(
    string ContractVersion,
    string Date,
    string Timezone,
    IReadOnlyDictionary<string, int> Counters,
    IReadOnlyList<ActivityPublicEvent> RecentEvents,
    int FeedLimit);

public sealed class ActivityQueryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
