namespace TwseWidget.Services;

public enum MarketSession
{
    Weekend,
    PreOpen,
    Open,
    PostClose
}

public static class TaiwanMarketClock
{
    private static readonly TimeSpan OpenTime = new(9, 0, 0);
    private static readonly TimeSpan CloseTime = new(13, 30, 0);

    public static TimeZoneInfo TaiwanTimeZone { get; } = FindTaiwanTimeZone();

    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TaiwanTimeZone);

    public static DateTimeOffset ToTaiwanTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, TaiwanTimeZone);

    public static MarketSession GetSession(DateTimeOffset value)
    {
        var taiwanTime = ToTaiwanTime(value);

        if (taiwanTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return MarketSession.Weekend;
        }

        if (taiwanTime.TimeOfDay < OpenTime)
        {
            return MarketSession.PreOpen;
        }

        if (taiwanTime.TimeOfDay <= CloseTime)
        {
            return MarketSession.Open;
        }

        return MarketSession.PostClose;
    }

    public static bool IsTradingSession(DateTimeOffset value) => GetSession(value) == MarketSession.Open;

    public static DateTimeOffset BuildMarketDateTime(string? date, string? time)
    {
        if (DateTime.TryParseExact(
                $"{date} {time}",
                "yyyyMMdd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
        {
            var unspecified = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            var offset = TaiwanTimeZone.GetUtcOffset(unspecified);
            return new DateTimeOffset(unspecified, offset);
        }

        return Now;
    }

    private static TimeZoneInfo FindTaiwanTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Taipei Standard Time",
                TimeSpan.FromHours(8),
                "Taipei Standard Time",
                "Taipei Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Taipei Standard Time",
                TimeSpan.FromHours(8),
                "Taipei Standard Time",
                "Taipei Standard Time");
        }
    }
}
