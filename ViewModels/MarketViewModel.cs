using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TwseWidget.Models;
using TwseWidget.Services;

namespace TwseWidget.ViewModels;

public sealed class MarketViewModel : INotifyPropertyChanged
{
    private MarketInstrument _instrument = MarketInstrument.Taiex;
    private MarketSnapshot? _snapshot;
    private MarketSession _session;
    private bool _isRefreshing;
    private string _lastError = string.Empty;
    private bool _isTopmost = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SymbolText { get; private set; } = MarketInstrument.Taiex.SymbolText;

    public string IndexTitleText { get; private set; } = MarketInstrument.Taiex.Name;

    public string UnitText => _instrument.IsIndex ? "PTS" : "元";

    public string CompactSessionText => _session switch
    {
        MarketSession.Open => $"盤中 · {_snapshot?.MarketTime ?? "--:--"}",
        MarketSession.PostClose => $"已收盤 · {_snapshot?.MarketTime ?? "--:--"}",
        MarketSession.PreOpen => "尚未開盤",
        _ => "休市"
    };

    public string CompactStatusDetailText => $"{SessionDetailText}\n{UpdateTimeText}\n{ErrorText}";

    public string CompactPinText => _isTopmost ? "PIN" : "OFF";

    public string ValueText { get; private set; } = "—";

    public string ChangeText { get; private set; } = "—";

    public string PercentText { get; private set; } = "—";

    public string TrendGlyph { get; private set; } = "•";

    public string OpenText { get; private set; } = "—";

    public string HighText { get; private set; } = "—";

    public string LowText { get; private set; } = "—";

    public string PreviousCloseText { get; private set; } = "—";

    public string RangeText { get; private set; } = "日內區間等待資料";

    public string TimeText { get; private set; } = "--:--:--";

    public string UpdateTimeText { get; private set; } = "尚未同步";

    public string LastSyncText { get; private set; } = "等待第一次同步";

    public string SessionBadgeText { get; private set; } = "等待市場狀態";

    public string SessionDetailText { get; private set; } = "Market session / checking";

    public string StatusText { get; private set; } = "WAITING";

    public string PollingText =>
        $"AUTO SYNC • {MarketRefreshPolicy.PollInterval.TotalSeconds:0}s";

    public string DataSourceText { get; private set; } = "TWSE MIS • tse_t00.tw";

    public string ErrorText { get; private set; } = string.Empty;

    public string TopmostText => _isTopmost ? "PINNED" : "FLOAT";

    public Brush StatusBrush { get; private set; } = Brushes.SlateGray;

    public Brush ChangeBrush { get; private set; } = Brushes.SlateGray;

    public bool IsPositive { get; private set; }

    public bool IsNegative { get; private set; }

    public bool HasData => _snapshot is not null;

    public bool IsRefreshing => _isRefreshing;

    public void SetInstrument(MarketInstrument instrument)
    {
        _instrument = instrument;
        _snapshot = null;
        _lastError = string.Empty;
        SymbolText = instrument.SymbolText;
        IndexTitleText = instrument.Name;
        DataSourceText = $"TWSE MIS • {instrument.QuoteChannel}";
        ValueText = "—";
        ChangeText = "—";
        PercentText = "—";
        TrendGlyph = "•";
        OpenText = "—";
        HighText = "—";
        LowText = "—";
        PreviousCloseText = "—";
        RangeText = "日內區間等待資料";
        UpdateTimeText = "尚未同步";
        LastSyncText = "等待第一次同步";
        ErrorText = string.Empty;
        IsPositive = false;
        IsNegative = false;
        ChangeBrush = Brushes.SlateGray;
        UpdateStatus();
        NotifyAll();
    }

    public void UpdateClock(DateTimeOffset now)
    {
        var taiwanNow = TaiwanMarketClock.ToTaiwanTime(now);
        TimeText = taiwanNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _session = TaiwanMarketClock.GetSession(taiwanNow);

        (SessionBadgeText, SessionDetailText) = _session switch
        {
            MarketSession.Open => ("MARKET OPEN", "盤中交易 • 09:00 — 13:30"),
            MarketSession.PreOpen => ("PRE-MARKET", "尚未開盤 • 今日 09:00 開始"),
            MarketSession.PostClose => ("MARKET CLOSED", "已收盤 • 下次交易日 09:00"),
            _ => ("WEEKEND", "週末休市 • 下個工作日再見")
        };

        UpdateStatus();
        NotifyAll();
    }

    public void ApplySnapshot(MarketSnapshot snapshot)
    {
        _snapshot = snapshot;
        _lastError = string.Empty;
        SymbolText = snapshot.IsIndex
            ? MarketInstrument.Taiex.SymbolText
            : $"{snapshot.InstrumentCode} / {snapshot.Exchange.ToUpperInvariant()}";
        IndexTitleText = snapshot.InstrumentName;
        DataSourceText = snapshot.Source;

        var change = snapshot.Current - snapshot.PreviousClose;
        var percent = snapshot.PreviousClose == 0
            ? 0
            : change / snapshot.PreviousClose * 100;

        ValueText = Format(snapshot.Current);
        ChangeText = FormatSigned(change);
        PercentText = $"{(percent >= 0 ? "+" : string.Empty)}{percent.ToString("0.00", CultureInfo.InvariantCulture)}%";
        TrendGlyph = change > 0 ? "▲" : change < 0 ? "▼" : "•";
        OpenText = Format(snapshot.Open);
        HighText = Format(snapshot.High);
        LowText = Format(snapshot.Low);
        PreviousCloseText = Format(snapshot.PreviousClose);
        RangeText = $"日內 {Format(snapshot.Low)} — {Format(snapshot.High)}";
        UpdateTimeText = $"行情時間 {snapshot.MarketTime}";
        LastSyncText = $"上次同步 {TaiwanMarketClock.Now:HH:mm:ss}";
        ErrorText = string.Empty;
        IsPositive = change > 0;
        IsNegative = change < 0;
        ChangeBrush = IsPositive ? Brushes.SpringGreen : IsNegative ? Brushes.Tomato : Brushes.SlateGray;

        UpdateStatus();
        NotifyAll();
    }

    public void ApplyError(Exception exception)
    {
        _lastError = exception.GetBaseException().Message;
        ErrorText = HasData ? "同步失敗，暫留上次數值" : "尚未取得 TWSE 資料";
        UpdateStatus();
        NotifyAll();
    }

    public void ApplyHistoryWarning()
    {
        // The quote is still valid. Keep it visible while making a failed history
        // backfill diagnosable instead of silently showing a misleading diagonal.
        ErrorText = "歷史線圖暫時無法取得，已保留目前走勢";
        NotifyAll();
    }

    public void SetRefreshing(bool isRefreshing)
    {
        _isRefreshing = isRefreshing;
        UpdateStatus();
        NotifyAll();
    }

    public void SetTopmost(bool isTopmost)
    {
        _isTopmost = isTopmost;
        NotifyAll();
    }

    private void UpdateStatus()
    {
        if (_isRefreshing)
        {
            StatusText = "SYNC";
            StatusBrush = Brushes.DeepSkyBlue;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastError))
        {
            StatusText = HasData ? "STALE" : "OFFLINE";
            StatusBrush = Brushes.Orange;
            return;
        }

        if (!HasData)
        {
            StatusText = "WAITING";
            StatusBrush = Brushes.SlateGray;
            return;
        }

        if (_session == MarketSession.Open)
        {
            // LAST means the endpoint was reached but the response did not
            // contain a new direct trade price. It is different from a local
            // cache and avoids labelling an open-market carry-forward as stale.
            StatusText = _snapshot?.IsLive == true ? "LIVE" : "LAST";
            StatusBrush = _snapshot?.IsLive == true
                ? Brushes.SpringGreen
                : Brushes.DeepSkyBlue;
            return;
        }

        StatusText = "CACHED";
        StatusBrush = Brushes.DeepSkyBlue;
    }

    private static string Format(decimal value) =>
        value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string FormatSigned(decimal value) =>
        $"{(value >= 0 ? "+" : string.Empty)}{Format(value)}";

    private void NotifyAll([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
