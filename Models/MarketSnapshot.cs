namespace TwseWidget.Models;

public sealed record MarketSnapshot
{
    public string InstrumentCode { get; init; } = "t00";

    public string InstrumentName { get; init; } = "發行量加權股價指數";

    public string Exchange { get; init; } = "tse";

    public bool IsIndex { get; init; } = true;

    public bool IsLive { get; init; } = true;

    public DateTimeOffset ObservedAt { get; init; }

    // The quote's ObservedAt is the last market transaction time. FetchedAt is
    // when this snapshot reached the widget and is used to keep the chart's
    // timeline moving even when there is no new trade.
    public DateTimeOffset FetchedAt { get; init; }

    public decimal Current { get; init; }

    public decimal PreviousClose { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public string MarketTime { get; init; } = "--:--:--";

    public string Source { get; init; } = "TWSE MIS";
}

public sealed record ChartPoint(DateTimeOffset Timestamp, decimal Value);
