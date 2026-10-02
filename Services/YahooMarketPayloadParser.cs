using System.Globalization;
using System.Text.Json;
using TwseWidget.Models;

namespace TwseWidget.Services;

public static class YahooMarketPayloadParser
{
    public static IReadOnlyList<ChartPoint> ParseIntradayHistory(
        string payload,
        DateTimeOffset sessionDate)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0 ||
            results[0].ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<ChartPoint>();
        }

        var result = results[0];
        if (!result.TryGetProperty("timestamp", out var timestamps) ||
            timestamps.ValueKind != JsonValueKind.Array ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) ||
            quotes.ValueKind != JsonValueKind.Array ||
            quotes.GetArrayLength() == 0 ||
            !quotes[0].TryGetProperty("close", out var closes) ||
            closes.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ChartPoint>();
        }

        var targetDate = TaiwanMarketClock.ToTaiwanTime(sessionDate).Date;
        var sessionStart = new DateTimeOffset(
            targetDate.AddHours(9),
            sessionDate.Offset);
        var sessionEnd = new DateTimeOffset(
            targetDate.AddHours(13).AddMinutes(30),
            sessionDate.Offset);
        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
        var points = new List<ChartPoint>(count);

        for (var index = 0; index < count; index++)
        {
            if (!TryReadInt64(timestamps[index], out var epochSeconds) ||
                !TryReadDecimal(closes[index], out var close))
            {
                continue;
            }

            var timestamp = TaiwanMarketClock.ToTaiwanTime(
                DateTimeOffset.FromUnixTimeSeconds(epochSeconds));
            if (timestamp < sessionStart || timestamp > sessionEnd)
            {
                continue;
            }

            points.Add(new ChartPoint(timestamp, close));
        }

        return points
            .OrderBy(point => point.Timestamp)
            .GroupBy(point => point.Timestamp)
            .Select(group => group.Last())
            .ToArray();
    }

    private static bool TryReadInt64(JsonElement value, out long result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result))
        {
            return true;
        }

        return long.TryParse(
            value.ValueKind == JsonValueKind.String ? value.GetString() : null,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryReadDecimal(JsonElement value, out decimal result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out result))
        {
            return true;
        }

        return decimal.TryParse(
            value.ValueKind == JsonValueKind.String ? value.GetString() : null,
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out result);
    }
}
