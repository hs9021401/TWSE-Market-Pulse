using System.Globalization;
using System.Text.Json;
using TwseWidget.Models;

namespace TwseWidget.Services;

public static class TwseMarketPayloadParser
{
    public static IReadOnlyList<ChartPoint> ParseIntradayHistory(
        string payload,
        DateTimeOffset fallbackDate)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("ohlcArray", out var history) ||
            history.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ChartPoint>();
        }

        var points = new List<ChartPoint>();
        foreach (var item in history.EnumerateArray())
        {
            var value = ReadDecimal(item, "c");
            if (value is null)
            {
                continue;
            }

            points.Add(new ChartPoint(ParseTimestamp(item, fallbackDate), value.Value));
        }

        return points
            .OrderBy(point => point.Timestamp)
            .GroupBy(point => point.Timestamp)
            .Select(group => group.Last())
            .ToArray();
    }

    private static DateTimeOffset ParseTimestamp(JsonElement item, DateTimeOffset fallbackDate)
    {
        var epochText = ReadString(item, "t");
        if (long.TryParse(epochText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            return TaiwanMarketClock.ToTaiwanTime(DateTimeOffset.FromUnixTimeMilliseconds(epoch));
        }

        var compactTime = ReadString(item, "ts") ?? ReadString(item, "time");
        if (DateTime.TryParseExact(
                compactTime,
                compactTime?.Length == 6 ? "HHmmss" : "HHmm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedTime))
        {
            return TaiwanMarketClock.BuildMarketDateTime(
                fallbackDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                parsedTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }

        return fallbackDate;
    }

    private static string? ReadString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement item, string propertyName)
    {
        var value = ReadString(item, propertyName);
        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return null;
        }

        return decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }
}
