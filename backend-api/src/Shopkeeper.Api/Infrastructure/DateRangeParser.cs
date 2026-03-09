using NodaTime;
using NodaTime.Text;

namespace Shopkeeper.Api.Infrastructure;

internal static class DateRangeParser
{
    private static readonly LocalDatePattern IsoPattern = LocalDatePattern.Iso;

    public static Instant? ParseDateStart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = IsoPattern.Parse(value);
        if (!result.Success) return null;
        return result.Value.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
    }

    public static Instant? ParseDateEnd(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = IsoPattern.Parse(value);
        if (!result.Success) return null;
        return result.Value.PlusDays(1).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant() - Duration.FromTicks(1);
    }

    public static (Instant fromUtc, Instant toUtc, string? error) Resolve(string? fromRaw, string? toRaw)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var defaultFrom = now - Duration.FromDays(30);
        var defaultTo = now;
        var from = ParseDateStart(fromRaw) ?? defaultFrom;
        var to = ParseDateEnd(toRaw) ?? defaultTo;
        if (from > to)
        {
            return (defaultFrom, defaultTo, "'from' date must be before or equal to 'to' date.");
        }
        return (from, to, null);
    }
}
