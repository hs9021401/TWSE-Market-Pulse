using System.Threading;
using System.Windows;
using System.Windows.Controls;
using TwseWidget.Models;
using TwseWidget.Services;

var startupTime = new DateTimeOffset(2026, 9, 8, 9, 58, 30, TimeSpan.FromHours(8));
var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "intraday-0958.json");
var historyPayload = await File.ReadAllTextAsync(fixturePath);
var history = TwseMarketPayloadParser.ParseIntradayHistory(historyPayload, startupTime);
var stockHistoryPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "stock-intraday.json");
var stockHistoryPayload = await File.ReadAllTextAsync(stockHistoryPath);
var stockHistory = YahooMarketPayloadParser.ParseIntradayHistory(stockHistoryPayload, startupTime);
var snapshot = new MarketSnapshot
{
    ObservedAt = startupTime,
    Current = 102.10m,
    PreviousClose = 99.00m,
    Open = 100.00m,
    High = 102.10m,
    Low = 99.90m
};

var merged = MarketHistory.MergeSessionPoints(history, snapshot);
var sessionStart = new TimeSpan(9, 0, 0);
var hasSessionStart = merged.Any(point => point.Timestamp.TimeOfDay == sessionStart);
var hasPreStartupHistory = merged.Any(point => point.Timestamp.TimeOfDay < startupTime.TimeOfDay);
var hasStockHistoryBeforeSelection = stockHistory.Count == 3 &&
                                     stockHistory[0].Timestamp.TimeOfDay == new TimeSpan(9, 0, 0) &&
                                     stockHistory[^1].Timestamp.TimeOfDay == new TimeSpan(9, 3, 0);
var staleTradeTime = TaiwanMarketClock.BuildMarketDateTime("20260908", "10:27:00");
var fetchedAt = TaiwanMarketClock.BuildMarketDateTime("20260908", "10:47:00");
var staleQuoteHistory = new[]
{
    new ChartPoint(
        TaiwanMarketClock.BuildMarketDateTime("20260908", "09:00:00"),
        108.10m),
    new ChartPoint(staleTradeTime, 109.20m),
    new ChartPoint(
        TaiwanMarketClock.BuildMarketDateTime("20260908", "10:29:00"),
        109.35m)
};
var staleQuote = new MarketSnapshot
{
    ObservedAt = staleTradeTime,
    FetchedAt = fetchedAt,
    Current = 109.65m,
    PreviousClose = 108.00m,
    Open = 108.10m,
    High = 109.65m,
    Low = 108.10m
};
var staleQuoteMerged = MarketHistory.MergeSessionPoints(staleQuoteHistory, staleQuote);
var staleQuoteTimelineMoves = staleQuoteMerged.Any(point => point.Timestamp == fetchedAt);
var staleQuoteGapIsFilled = staleQuoteMerged.Any(point =>
    point.Timestamp == fetchedAt - MarketRefreshPolicy.PollInterval);
var staleQuoteInternalGapIsFilled = staleQuoteMerged.Any(point =>
    point.Timestamp == TaiwanMarketClock.BuildMarketDateTime("20260908", "10:27:20") &&
    point.Value == 109.20m);
var pollIntervalIsTwentySeconds = MarketRefreshPolicy.PollInterval == TimeSpan.FromSeconds(20);
var postCloseManualReloadsHistory = MarketRefreshPolicy.ShouldBackfillHistory(
    isFirstSessionLoad: false,
    MarketSession.PostClose,
    forceHistoryReload: true);
var openInitialLoadBackfillsHistory = MarketRefreshPolicy.ShouldBackfillHistory(
    isFirstSessionLoad: true,
    MarketSession.Open,
    forceHistoryReload: false);
var preOpenManualRefreshSkipsHistory = !MarketRefreshPolicy.ShouldBackfillHistory(
    isFirstSessionLoad: false,
    MarketSession.PreOpen,
    forceHistoryReload: true);
var etfHistoryRetainsPrices = VerifyLateStartingHistory("00881", 52.50m, 52.70m, 52.60m, 52.20m) &
                             VerifyLateStartingHistory("00919", 32.03m, 32.04m, 31.95m, 31.88m);
var settingsPersist = TwseWidget.Tests.WidgetSettingsRegression.Run();
var layoutsWork = RunOnSta(TwseWidget.Tests.WidgetLayoutRegression.Run);
var blankSurfaceCanDrag = DragSurfacePolicy.ShouldBeginDrag(isInteractiveElement: false);
var buttonSurfaceCanDrag = DragSurfacePolicy.ShouldBeginDrag(isInteractiveElement: true);
var (windowSurfaceCanDrag, textBoxSurfaceCanDrag) = RunOnSta(() =>
(
    DragSurfacePolicy.ShouldBeginDrag(new Window()),
    DragSurfacePolicy.ShouldBeginDrag(new TextBox())
));

using var lifetimeCancellation = new CancellationTokenSource();
using var searchCancellation = new SearchCancellationCoordinator();
var firstSearch = searchCancellation.Start(lifetimeCancellation.Token);
searchCancellation.Finish(firstSearch);
searchCancellation.CancelCurrent();
var secondSearch = searchCancellation.Start(lifetimeCancellation.Token);
var replacedSearch = searchCancellation.Start(lifetimeCancellation.Token);
var searchCancellationIsSafe = secondSearch.IsCancellationRequested &&
                               searchCancellation.IsCurrent(replacedSearch);
searchCancellation.Finish(replacedSearch);

var etfSearchCandidates = new[]
{
    new StockSearchResult { Code = "00878", Name = "國泰永續高股息", Exchange = "tse" },
    new StockSearchResult { Code = "00881", Name = "國泰台灣科技龍頭", Exchange = "tse" },
    new StockSearchResult { Code = "00403A", Name = "主動統一升級50", Exchange = "tse" },
    new StockSearchResult { Code = "00830", Name = "國泰費城半導體", Exchange = "tse" },
    new StockSearchResult { Code = "00834B", Name = "第一金金融債10+", Exchange = "otc" },
    new StockSearchResult { Code = "00836B", Name = "永豐10年A公司債", Exchange = "otc" },
    new StockSearchResult { Code = "00840B", Name = "凱基IG精選15+", Exchange = "otc" },
    new StockSearchResult { Code = "00841B", Name = "凱基AAA-AA公司債", Exchange = "otc" },
    new StockSearchResult { Code = "00842B", Name = "台新美元銀行債", Exchange = "otc" },
    new StockSearchResult { Code = "00844B", Name = "台新15年IG金融債", Exchange = "otc" },
    new StockSearchResult { Code = "00845B", Name = "富邦新興投等債", Exchange = "otc" },
    new StockSearchResult { Code = "00846B", Name = "富邦歐洲銀行債", Exchange = "otc" },
    new StockSearchResult { Code = "00847B", Name = "中信美國市政債", Exchange = "otc" },
    new StockSearchResult { Code = "00848B", Name = "中信新興亞洲債", Exchange = "otc" },
    new StockSearchResult { Code = "00849B", Name = "中信EM主權債0-5", Exchange = "otc" },
    new StockSearchResult { Code = "2330", Name = "台積電", Exchange = "tse" },
    new StockSearchResult { Code = "00879", Name = "測試認購權證", Exchange = "tse" }
};
var etfSearchResults = TwseMarketService.FilterSearchResults(etfSearchCandidates, "008");
var etfSearchWorks = etfSearchResults.Any(item => item.Code == "00878") &&
                     etfSearchResults.Any(item => item.Code == "00881") &&
                     etfSearchResults.All(item => item.Code != "00879");
var alphanumericSearchResults = TwseMarketService.FilterSearchResults(
    etfSearchCandidates,
    "00403A");
var alphanumericSearchWorks = alphanumericSearchResults.Any(item => item.Code == "00403A");

var watchlistPersistencePath = Path.Combine(
    Path.GetTempPath(),
    $"twse-widget-watchlist-{Guid.NewGuid():N}.json");
var watchlistStore = new WatchlistStore(watchlistPersistencePath);
var persistedInstrument = new MarketInstrument
{
    Code = "2330",
    Name = "台積電",
    Exchange = "tse",
    IsIndex = false
};
watchlistStore.Save(new[] { MarketInstrument.Taiex, persistedInstrument });
var loadedWatchlist = watchlistStore.Load();
var watchlistPersists = loadedWatchlist.Count == 1 &&
                        loadedWatchlist[0].Key.Equals(
                            persistedInstrument.Key,
                            StringComparison.OrdinalIgnoreCase);
try
{
    File.Delete(watchlistPersistencePath);
}
catch (IOException)
{
}
catch (UnauthorizedAccessException)
{
}

if (!hasSessionStart ||
    !hasPreStartupHistory ||
    merged.Count < 4 ||
    !hasStockHistoryBeforeSelection ||
    !staleQuoteTimelineMoves ||
    !staleQuoteGapIsFilled ||
    !staleQuoteInternalGapIsFilled ||
    !pollIntervalIsTwentySeconds ||
    !postCloseManualReloadsHistory ||
    !openInitialLoadBackfillsHistory ||
    !preOpenManualRefreshSkipsHistory ||
    !etfHistoryRetainsPrices ||
    !blankSurfaceCanDrag ||
    buttonSurfaceCanDrag ||
    !windowSurfaceCanDrag ||
    textBoxSurfaceCanDrag ||
    !searchCancellationIsSafe ||
    !etfSearchWorks ||
    !alphanumericSearchWorks ||
    !watchlistPersists ||
    !settingsPersist ||
    !layoutsWork)
{
    Console.Error.WriteLine(
        $"FAIL: regression detected; points={merged.Count}, staleTimeline={staleQuoteTimelineMoves}, staleGap={staleQuoteGapIsFilled}, internalGap={staleQuoteInternalGapIsFilled}, poll20s={pollIntervalIsTwentySeconds}, postCloseReload={postCloseManualReloadsHistory}, openInitial={openInitialLoadBackfillsHistory}, preOpenSkip={preOpenManualRefreshSkipsHistory}, blank={blankSurfaceCanDrag}, button={buttonSurfaceCanDrag}, window={windowSurfaceCanDrag}, textBox={textBoxSurfaceCanDrag}, searchSafe={searchCancellationIsSafe}, etfSearch={etfSearchWorks}, alphanumericSearch={alphanumericSearchWorks}, watchlistPersists={watchlistPersists}.");
    return 1;
}

Console.WriteLine(
    $"PASS: history (including late-starting ETF prices), drag, ETF search, search cancellation, and watchlist persistence regressions are covered; startup {startupTime:HH:mm:ss} produces {merged.Count} points.");
return 0;

static bool VerifyLateStartingHistory(
    string code, decimal open, decimal firstPrice, decimal laterPrice, decimal close)
{
    // Reproduce the 00881/00919 feed shape: the first historical point is
    // 09:02, not 09:00. Prices are deterministic samples, not live API data.
    var start = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(8));
    var end = start.AddHours(4).AddMinutes(30);
    var snapshot = new MarketSnapshot
    {
        InstrumentCode = code,
        IsIndex = false,
        ObservedAt = end,
        FetchedAt = end.AddHours(3),
        Open = open,
        Current = close
    };
    var history = new[]
    {
        new ChartPoint(start.AddMinutes(3), laterPrice),
        new ChartPoint(start.AddMinutes(2), firstPrice),
        new ChartPoint(end.AddMinutes(-1), close),
        new ChartPoint(end, close + 0.001m)
    };
    var merged = MarketHistory.MergeSessionPoints(history, snapshot);
    var lost = history.Where(point => point.Timestamp < end)
        .Count(point => !merged.Contains(point));
    var openingRetained = merged[0] == new ChartPoint(start, open);
    var gapsRetainLastPrice = merged.Contains(new ChartPoint(start.AddSeconds(20), open)) &&
                             merged.Contains(new ChartPoint(start.AddMinutes(2).AddSeconds(20), firstPrice));
    var orderedAndUnique = merged.Zip(merged.Skip(1), (left, right) => left.Timestamp < right.Timestamp)
        .All(ordered => ordered);
    var closingQuoteWins = merged[^1] == new ChartPoint(end, close);
    var refreshIsStable = merged.SequenceEqual(MarketHistory.MergeSessionPoints(merged, snapshot));
    var success = lost == 0 && openingRetained && gapsRetainLastPrice &&
                  orderedAndUnique && closingQuoteWins && refreshIsStable;
    Console.WriteLine(
        $"{(success ? "PASS" : "FAIL")}: {code} late-start history; lostPrices={lost}, gapPrices={gapsRetainLastPrice}, ordered={orderedAndUnique}, close={closingQuoteWins}, repeatRefresh={refreshIsStable}.");
    return success;
}

static T RunOnSta<T>(Func<T> action)
{
    T? result = default;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            result = action();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        throw new InvalidOperationException("STA regression probe failed.", failure);
    }

    return result!;
}
