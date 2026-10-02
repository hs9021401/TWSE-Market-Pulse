using TwseWidget.Models;

namespace TwseWidget.Services;

public static class MarketHistory
{
    public static IReadOnlyList<ChartPoint> MergeSessionPoints(
        IEnumerable<ChartPoint> history,
        MarketSnapshot snapshot)
    {
        var chartTimestamp = snapshot.FetchedAt == default
            ? snapshot.ObservedAt
            : snapshot.FetchedAt;
        chartTimestamp = TaiwanMarketClock.ToTaiwanTime(chartTimestamp);

        var sessionStart = new DateTimeOffset(
            chartTimestamp.Date.AddHours(9),
            chartTimestamp.Offset);
        var sessionEnd = new DateTimeOffset(
            chartTimestamp.Date.AddHours(13).AddMinutes(30),
            chartTimestamp.Offset);
        var chartEnd = chartTimestamp < sessionStart
            ? sessionStart
            : chartTimestamp > sessionEnd
                ? sessionEnd
                : chartTimestamp;

        var points = history
            .Select(point => new ChartPoint(
                TaiwanMarketClock.ToTaiwanTime(point.Timestamp),
                point.Value))
            .Where(point => point.Timestamp >= sessionStart && point.Timestamp <= chartEnd)
            .OrderBy(point => point.Timestamp)
            .GroupBy(point => point.Timestamp)
            .Select(group => group.Last())
            .ToList();

        if (!points.Any(point => point.Timestamp == sessionStart))
        {
            // History is sorted and restricted to this session. Prepend the
            // missing opening point so gap filling never runs backwards from
            // the last historical tick to 09:00 and overwrites actual prices.
            points.Insert(0, new ChartPoint(sessionStart, snapshot.Open));
        }

        // A quote's market timestamp is the last trade time, not the time at
        // which this refresh succeeded. Keep the whole chart timeline
        // continuous by carrying the last known value forward at the polling
        // cadence through both internal and trailing data gaps.
        points = FillMissingIntervals(points, chartEnd);

        points.Add(new ChartPoint(chartEnd, snapshot.Current));

        return points
            .Where(point => point.Timestamp >= sessionStart && point.Timestamp <= sessionEnd)
            .OrderBy(point => point.Timestamp)
            .GroupBy(point => point.Timestamp)
            .Select(group => group.Last())
            .ToArray();
    }

    private static List<ChartPoint> FillMissingIntervals(
        IReadOnlyList<ChartPoint> points,
        DateTimeOffset chartEnd)
    {
        if (points.Count == 0)
        {
            return new List<ChartPoint>();
        }

        var completed = new List<ChartPoint>(points.Count + 16)
        {
            points[0]
        };

        for (var index = 1; index < points.Count; index++)
        {
            var previous = completed[^1];
            var current = points[index];
            var nextTimestamp = previous.Timestamp + MarketRefreshPolicy.PollInterval;
            while (nextTimestamp < current.Timestamp)
            {
                completed.Add(new ChartPoint(nextTimestamp, previous.Value));
                nextTimestamp += MarketRefreshPolicy.PollInterval;
            }

            completed.Add(current);
        }

        var lastKnown = completed[^1];
        var trailingTimestamp = lastKnown.Timestamp + MarketRefreshPolicy.PollInterval;
        while (trailingTimestamp < chartEnd)
        {
            completed.Add(new ChartPoint(trailingTimestamp, lastKnown.Value));
            trailingTimestamp += MarketRefreshPolicy.PollInterval;
        }

        return completed;
    }
}
