namespace TwseWidget.Services;

public static class MarketRefreshPolicy
{
    public static TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(20);

    public static bool ShouldBackfillHistory(
        bool isFirstSessionLoad,
        MarketSession session,
        bool forceHistoryReload) =>
        session is MarketSession.Open or MarketSession.PostClose &&
        (isFirstSessionLoad || forceHistoryReload);
}
