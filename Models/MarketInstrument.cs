namespace TwseWidget.Models;

public sealed record MarketInstrument
{
    public string Code { get; init; } = "t00";

    public string Name { get; init; } = "發行量加權股價指數";

    public string Exchange { get; init; } = "tse";

    public bool IsIndex { get; init; } = true;

    public string Channel => IsIndex ? "t00.tw" : $"{Code}.tw";

    public string QuoteChannel => $"{Exchange}_{Channel}";

    public string Key => $"{Exchange}:{Code}";

    public string DisplayText => IsIndex ? "TAIEX  ·  大盤" : $"{Code}  ·  {Name}";

    public string SymbolText => IsIndex ? "TAIEX / CASH" : $"{Code} / {Exchange.ToUpperInvariant()}";

    public static MarketInstrument Taiex { get; } = new();
}

public sealed record StockSearchResult
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Exchange { get; init; } = "tse";

    public string DisplayText => $"{Code}  ·  {Name}";

    public MarketInstrument ToInstrument() => new()
    {
        Code = Code,
        Name = Name,
        Exchange = Exchange,
        IsIndex = false
    };
}
