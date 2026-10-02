using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;
using TwseWidget.Models;
using TwseWidget.Services;
using TwseWidget.ViewModels;

namespace TwseWidget;

public partial class MainWindow : Window
{
    private readonly TwseMarketService _marketService = new();
    private readonly DrawingIcon _trayIconImage;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly MarketViewModel _viewModel = new();
    private readonly WatchlistStore _watchlistStore;
    private readonly WidgetSettingsStore _settingsStore;
    private WidgetSettings _settings;
    private WidgetLayout _layout = WidgetLayout.Full;
    private bool _applyingLayout;
    private bool _placementReady;
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly List<ChartPoint> _chartPoints = new();
    private readonly ObservableCollection<MarketInstrument> _watchlist = new();
    private readonly ObservableCollection<StockSearchResult> _searchResults = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SearchCancellationCoordinator _searchCancellation = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = MarketRefreshPolicy.PollInterval };
    private MarketInstrument _selectedInstrument = MarketInstrument.Taiex;
    private StockSearchResult? _selectedSearchResult;
    private bool _isFetching;
    private bool _suppressSearchTextChanged;
    private bool _isUpdatingWatchlistSelection;
    private bool _refreshPending;
    private bool _historyRefreshPending;
    private CancellationTokenSource? _activeRefreshCancellation;
    private DateTime? _chartDate;
    private string? _chartInstrumentKey;
    private int _selectionVersion;

    public MainWindow() : this(new WidgetSettingsStore(), new WatchlistStore())
    {
    }

    internal MainWindow(WidgetSettingsStore settingsStore, WatchlistStore watchlistStore, bool showTrayIcon = true)
    {
        _settingsStore = settingsStore;
        _watchlistStore = watchlistStore;
        InitializeComponent();
        _settings = _settingsStore.Load();
        WindowShell.ContextMenu = CreateLayoutMenu();
        _trayIconImage = LoadTrayIcon();
        _trayMenu = CreateTrayMenu();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "TWSE Market Pulse",
            ContextMenuStrip = _trayMenu,
            Visible = showTrayIcon
        };
        _trayIcon.DoubleClick += TrayIcon_DoubleClick;

        DataContext = _viewModel;

        _watchlist.Add(MarketInstrument.Taiex);
        foreach (var instrument in _watchlistStore.Load())
        {
            if (!_watchlist.Any(item =>
                    item.Key.Equals(instrument.Key, StringComparison.OrdinalIgnoreCase)))
            {
                _watchlist.Add(instrument);
            }
        }

        foreach (var combo in WatchlistSelectors)
        {
            combo.ItemsSource = _watchlist;
        }
        SearchResultsList.ItemsSource = _searchResults;
        SynchronizeWatchlistSelection();
        _clockTimer.Tick += ClockTimer_Tick;
        _refreshTimer.Tick += RefreshTimer_Tick;

        _settingsTimer.Tick += (_, _) =>
        {
            _settingsTimer.Stop();
            SaveSettings();
        };
        Topmost = _settings.IsTopmost;
        _viewModel.SetTopmost(Topmost);
        SetLayout(_settings.Layout, persist: false);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_settings.Left is double left && _settings.Top is double top)
        {
            Left = left;
            Top = top;
        }
        KeepWindowOnScreen();
        _placementReady = true;
        UpdateClock();
        _clockTimer.Start();
        _refreshTimer.Start();
        QueueRefresh();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _clockTimer.Stop();
        _refreshTimer.Stop();
        _settingsTimer.Stop();
        SaveSettings();
        _watchlistStore.Save(_watchlist);
        _trayIcon.Visible = false;
        _trayIcon.DoubleClick -= TrayIcon_DoubleClick;
        _trayIcon.Dispose();
        _trayMenu.Dispose();
        _trayIconImage.Dispose();
        _searchCancellation.Dispose();
        _activeRefreshCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        _marketService.Dispose();
    }

    private void ClockTimer_Tick(object? sender, EventArgs e) => UpdateClock();

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (TaiwanMarketClock.IsTradingSession(TaiwanMarketClock.Now))
        {
            QueueRefresh();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        QueueRefresh(prioritize: true, refreshHistory: true);

    private void QueueRefresh(bool prioritize = false, bool refreshHistory = false)
    {
        if (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (_isFetching)
        {
            _refreshPending = true;
            _historyRefreshPending |= refreshHistory;
            if (prioritize)
            {
                _activeRefreshCancellation?.Cancel();
            }

            return;
        }

        _ = RefreshAsync(refreshHistory);
    }

    private async Task RefreshAsync(bool refreshHistory = false)
    {
        if (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (_isFetching)
        {
            _refreshPending = true;
            _historyRefreshPending |= refreshHistory;
            return;
        }

        var instrument = _selectedInstrument;
        var selectionVersion = _selectionVersion;
        var refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _activeRefreshCancellation = refreshCancellation;
        var refreshToken = refreshCancellation.Token;
        _isFetching = true;
        _viewModel.SetRefreshing(true);

        try
        {
            var snapshot = await _marketService.GetLatestAsync(
                instrument,
                refreshToken);
            refreshToken.ThrowIfCancellationRequested();

            if (selectionVersion != _selectionVersion ||
                instrument.Key != _selectedInstrument.Key)
            {
                return;
            }

            var chartObservationAt = snapshot.FetchedAt == default
                ? snapshot.ObservedAt
                : snapshot.FetchedAt;
            var chartDate = TaiwanMarketClock.ToTaiwanTime(chartObservationAt).Date;
            if (_chartDate != chartDate ||
                _chartInstrumentKey != instrument.Key)
            {
                _chartDate = chartDate;
                _chartInstrumentKey = instrument.Key;
                _chartPoints.Clear();
            }

            var isFirstSessionLoad = _chartPoints.Count == 0;
            // Apply the latest quote before waiting for the optional historical
            // backfill. Switching to a stock should show its current quote
            // immediately even if Yahoo/TWSE history is slow or unavailable.
            var liveMerged = MarketHistory.MergeSessionPoints(_chartPoints, snapshot);
            _chartPoints.Clear();
            _chartPoints.AddRange(liveMerged);
            TrimChartPoints();
            _viewModel.ApplySnapshot(snapshot);
            UpdateCharts();

            var now = TaiwanMarketClock.Now;
            var shouldBackfillHistory = MarketRefreshPolicy.ShouldBackfillHistory(
                isFirstSessionLoad,
                TaiwanMarketClock.GetSession(now),
                refreshHistory);
            if (shouldBackfillHistory &&
                TaiwanMarketClock.ToTaiwanTime(chartObservationAt).Date ==
                TaiwanMarketClock.ToTaiwanTime(now).Date)
            {
                try
                {
                    var intradayHistory = await _marketService.GetIntradayHistoryAsync(
                        instrument,
                        chartObservationAt,
                        refreshToken);
                    refreshToken.ThrowIfCancellationRequested();

                    if (selectionVersion != _selectionVersion ||
                        instrument.Key != _selectedInstrument.Key)
                    {
                        return;
                    }

                    var historyIsTooShortAfterClose =
                        TaiwanMarketClock.GetSession(TaiwanMarketClock.Now) == MarketSession.PostClose &&
                        intradayHistory.Count < 3;
                    if (intradayHistory.Count == 0 || historyIsTooShortAfterClose)
                    {
                        // Never replace an existing session chart with the
                        // carry-forward/live-only shape that can look like a
                        // misleading diagonal when history is temporarily blank.
                        _viewModel.ApplyHistoryWarning();
                        return;
                    }

                    var merged = MarketHistory.MergeSessionPoints(intradayHistory, snapshot);
                    _chartPoints.Clear();
                    _chartPoints.AddRange(merged);
                    TrimChartPoints();
                    UpdateCharts();
                }
                catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception) when (!refreshToken.IsCancellationRequested)
                {
                    if (selectionVersion != _selectionVersion ||
                        instrument.Key != _selectedInstrument.Key)
                    {
                        return;
                    }

                    // The live quote has already been applied and remains useful
                    // when historical backfill is unavailable. Do not replace an
                    // existing complete chart with a live-only diagonal.
                    _viewModel.ApplyHistoryWarning();
                }
            }

        }
        catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (!refreshToken.IsCancellationRequested &&
                                           selectionVersion == _selectionVersion &&
                                           instrument.Key == _selectedInstrument.Key)
        {
            _viewModel.ApplyError(exception);
        }
        finally
        {
            if (ReferenceEquals(_activeRefreshCancellation, refreshCancellation))
            {
                _activeRefreshCancellation = null;
            }

            _isFetching = false;
            _viewModel.SetRefreshing(false);
            refreshCancellation.Dispose();

            if (!_lifetimeCancellation.IsCancellationRequested && _refreshPending)
            {
                var nextRefreshHistory = _historyRefreshPending;
                _refreshPending = false;
                _historyRefreshPending = false;
                QueueRefresh(refreshHistory: nextRefreshHistory);
            }
        }
    }

    private void TrimChartPoints()
    {
        // One session contains at most 810 points at a 20-second cadence. Keep
        // a little headroom for one-minute historical points and replacements.
        while (_chartPoints.Count > 1200)
        {
            _chartPoints.RemoveAt(0);
        }
    }

    private void UpdateClock() => _viewModel.UpdateClock(TaiwanMarketClock.Now);

    private void UpdateCharts()
    {
        ChartA.SetData(_chartPoints, _viewModel.IsPositive, _viewModel.HasData);
        ChartMini.SetData(_chartPoints, _viewModel.IsPositive, _viewModel.HasData);
        ChartTicker.SetData(_chartPoints, _viewModel.IsPositive, _viewModel.HasData);
    }

    private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchTextBox.Text.Trim();
        SearchPlaceholder.Visibility = query.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_suppressSearchTextChanged)
        {
            return;
        }

        _selectedSearchResult = null;
        _searchCancellation.CancelCurrent();
        _searchResults.Clear();
        SearchResultsList.SelectedItem = null;
        SearchResultsList.Visibility = Visibility.Collapsed;

        if (query.Length == 0)
        {
            return;
        }

        var cancellation = _searchCancellation.Start(_lifetimeCancellation.Token);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(180), cancellation.Token);
            var results = await _marketService.SearchStocksAsync(query, cancellation.Token);

            if (cancellation.IsCancellationRequested ||
                !_searchCancellation.IsCurrent(cancellation) ||
                !string.Equals(query, SearchTextBox.Text.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            foreach (var result in results)
            {
                _searchResults.Add(result);
            }

            SearchResultsList.Visibility = _searchResults.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Search is auxiliary UI; a temporary search failure should not hide
            // or invalidate the last valid market quote.
            SearchResultsList.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _searchCancellation.Finish(cancellation);
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        AddWatchlist_Click(sender, e);
        e.Handled = true;
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is StockSearchResult result)
        {
            _selectedSearchResult = result;
            _suppressSearchTextChanged = true;
            SearchTextBox.Text = result.DisplayText;
            _suppressSearchTextChanged = false;
            SearchResultsList.Visibility = Visibility.Collapsed;
        }
    }

    private void AddWatchlist_Click(object sender, RoutedEventArgs e)
    {
        var result = _selectedSearchResult ??
                     SearchResultsList.SelectedItem as StockSearchResult ??
                     _searchResults.FirstOrDefault();
        if (result is null)
        {
            return;
        }

        var instrument = result.ToInstrument();
        var existing = _watchlist.FirstOrDefault(item =>
            item.Key.Equals(instrument.Key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _watchlist.Add(instrument);
            existing = instrument;
        }

        _watchlistStore.Save(_watchlist);
        SearchTextBox.Clear();
        SearchResultsList.Visibility = Visibility.Collapsed;
        SelectInstrument(existing, updateWatchlistSelection: true);
    }

    private void WatchlistComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingWatchlistSelection ||
            sender is not ComboBox combo ||
            combo.SelectedItem is not MarketInstrument instrument)
        {
            return;
        }

        if (instrument.Key.Equals(_selectedInstrument.Key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SearchResultsList.Visibility = Visibility.Collapsed;
        SelectInstrument(instrument, updateWatchlistSelection: true);
    }

    private void PreviousInstrument_Click(object sender, RoutedEventArgs e) =>
        SelectRelativeInstrument(-1);

    private void NextInstrument_Click(object sender, RoutedEventArgs e) =>
        SelectRelativeInstrument(1);

    private void SelectRelativeInstrument(int offset)
    {
        if (_watchlist.Count == 0)
        {
            return;
        }

        var currentIndex = _watchlist.IndexOf(_selectedInstrument);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = (currentIndex + offset) % _watchlist.Count;
        if (nextIndex < 0)
        {
            nextIndex += _watchlist.Count;
        }

        var next = _watchlist[nextIndex];
        if (!next.Key.Equals(_selectedInstrument.Key, StringComparison.OrdinalIgnoreCase))
        {
            SelectInstrument(next, updateWatchlistSelection: true);
        }
    }

    private void SelectInstrument(MarketInstrument instrument, bool updateWatchlistSelection)
    {
        _selectedInstrument = instrument;
        _selectionVersion++;
        _chartDate = null;
        _chartInstrumentKey = null;
        _chartPoints.Clear();
        _viewModel.SetInstrument(instrument);
        UpdateCharts();

        if (updateWatchlistSelection)
        {
            SynchronizeWatchlistSelection();
        }

        QueueRefresh(prioritize: true);
    }

    private void ToggleTopmost_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _viewModel.SetTopmost(Topmost);
        SaveSettings();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            !DragSurfacePolicy.ShouldBeginDrag(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // DragMove can be rejected while the window is changing state.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void TrayIcon_DoubleClick(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(ShowWidget));

    private void ShowWidget_Click(object? sender, EventArgs e) => ShowWidget();

    private void HideWidget_Click(object? sender, EventArgs e) => Hide();

    private void TrayRefresh_Click(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
            QueueRefresh(prioritize: true, refreshHistory: true)));

    private void TrayExit_Click(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(Close));

    private void ShowWidget()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        KeepWindowOnScreen();
        Activate();
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem("顯示 Widget");
        var hideItem = new Forms.ToolStripMenuItem("隱藏 Widget");
        var refreshItem = new Forms.ToolStripMenuItem("立即刷新");
        var exitItem = new Forms.ToolStripMenuItem("結束程式");

        showItem.Click += ShowWidget_Click;
        hideItem.Click += HideWidget_Click;
        refreshItem.Click += TrayRefresh_Click;
        exitItem.Click += TrayExit_Click;

        menu.Items.Add(showItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(refreshItem);
        var layouts = new Forms.ToolStripMenuItem("切換版面");
        foreach (var layout in Enum.GetValues<WidgetLayout>())
        {
            var item = new Forms.ToolStripMenuItem(LayoutLabel(layout)) { Tag = layout };
            item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => SetLayout(layout)));
            layouts.DropDownItems.Add(item);
        }
        menu.Opening += (_, _) =>
        {
            foreach (Forms.ToolStripMenuItem item in layouts.DropDownItems)
            {
                item.Checked = item.Tag is WidgetLayout layout && layout == _layout;
            }
        };
        menu.Items.Add(layouts);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private static DrawingIcon LoadTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/TwseWidget;component/Assets/TwseWidget.ico", UriKind.Absolute));
            if (resource is not null)
            {
                using (resource.Stream)
                using (var source = new DrawingIcon(resource.Stream))
                {
                    return (DrawingIcon)source.Clone();
                }
            }
        }
        catch (Exception)
        {
            // Fall back to the system icon if a resource cannot be loaded.
        }

        return (DrawingIcon)System.Drawing.SystemIcons.Application.Clone();
    }

    private IEnumerable<ComboBox> WatchlistSelectors =>
        new[] { WatchlistComboBox, MiniWatchlistComboBox, TickerWatchlistComboBox };

    private void SynchronizeWatchlistSelection()
    {
        _isUpdatingWatchlistSelection = true;
        try
        {
            foreach (var combo in WatchlistSelectors)
            {
                combo.SelectedItem = _selectedInstrument;
            }
        }
        finally
        {
            _isUpdatingWatchlistSelection = false;
        }
    }

    private static string LayoutLabel(WidgetLayout layout) => layout switch
    {
        WidgetLayout.Mini => "Mini 走勢卡 · 260 × 160",
        WidgetLayout.Ticker => "Ticker 橫條 · 280 × 88",
        _ => "完整模式 · 360 × 240"
    };

    private ContextMenu CreateLayoutMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource(typeof(ContextMenu)) };
        foreach (var layout in Enum.GetValues<WidgetLayout>())
        {
            var item = new MenuItem
            {
                Header = LayoutLabel(layout), IsCheckable = true, Tag = layout,
                Style = (Style)FindResource(typeof(MenuItem))
            };
            item.Click += (_, _) => SetLayout(layout);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = "新增自選股票…" };
        manage.Click += (_, _) =>
        {
            SetLayout(WidgetLayout.Full);
            SearchTextBox.Focus();
        };
        var refresh = new MenuItem { Header = "刷新行情與完整線圖" };
        refresh.Click += Refresh_Click;
        var pin = new MenuItem { Header = "切換置頂", IsCheckable = true };
        pin.Click += ToggleTopmost_Click;
        menu.Items.Add(manage);
        menu.Items.Add(refresh);
        menu.Items.Add(pin);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.Style = (Style)FindResource(typeof(MenuItem));
        }
        menu.Opened += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Tag is WidgetLayout layout)
                {
                    item.IsChecked = layout == _layout;
                }
            }
            pin.IsChecked = Topmost;
        };
        return menu;
    }

    private void LayoutPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement target)
        {
            var menu = CreateLayoutMenu();
            menu.PlacementTarget = target;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void SetLayout(WidgetLayout layout, bool persist = true)
    {
        if (!Enum.IsDefined(layout))
        {
            layout = WidgetLayout.Full;
        }
        _applyingLayout = true;
        try
        {
            if (_placementReady && _layout == WidgetLayout.Full)
            {
                _settings = _settings with
                {
                    FullWidth = Math.Clamp(Width, 320, 1600),
                    FullHeight = Math.Clamp(Height, 240, 1200)
                };
            }
            _layout = layout;
            SearchResultsList.Visibility = Visibility.Collapsed;
            // Lower minima before shrinking; WPF would otherwise coerce compact
            // windows to the old full-size minimum.
            MinWidth = 1;
            MinHeight = 1;
            VariantA.Visibility = layout == WidgetLayout.Full ? Visibility.Visible : Visibility.Collapsed;
            MiniLayout.Visibility = layout == WidgetLayout.Mini ? Visibility.Visible : Visibility.Collapsed;
            TickerLayout.Visibility = layout == WidgetLayout.Ticker ? Visibility.Visible : Visibility.Collapsed;
            ResizeMode = layout == WidgetLayout.Full ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
            (Width, Height) = layout switch
            {
                WidgetLayout.Mini => (260d, 160d),
                WidgetLayout.Ticker => (280d, 88d),
                _ => (_settings.FullWidth, _settings.FullHeight)
            };
            MinWidth = layout == WidgetLayout.Full ? 320 : Width;
            MinHeight = layout == WidgetLayout.Full ? 240 : Height;
            WindowShell.CornerRadius = new CornerRadius(layout == WidgetLayout.Full ? 20 : 14);
            if (_placementReady)
            {
                KeepWindowOnScreen();
            }
            // Charts already hold the same points; no quote/history fetch here.
            UpdateCharts();
        }
        finally
        {
            _applyingLayout = false;
        }
        if (persist && _placementReady)
        {
            SaveSettings();
        }
    }

    private void Window_PlacementChanged(object? sender, EventArgs e)
    {
        if (!_placementReady || _applyingLayout || WindowState != WindowState.Normal)
        {
            return;
        }
        _settingsTimer.Stop();
        _settingsTimer.Start();
    }

    private void KeepWindowOnScreen()
    {
        // Convert screen working areas from device pixels to WPF's logical units.
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var areas = Forms.Screen.AllScreens.Select(screen =>
        {
            var area = screen.WorkingArea;
            var topLeft = transform.Transform(new Point(area.Left, area.Top));
            var bottomRight = transform.Transform(new Point(area.Right, area.Bottom));
            return new Rect(topLeft, bottomRight);
        }).ToArray();
        var fitted = WidgetPlacementPolicy.Fit(
            new Rect(Left, Top, Width, Height),
            areas.Length == 0 ? new[] { SystemParameters.WorkArea } : areas);
        if (_layout == WidgetLayout.Full)
        {
            Width = Math.Max(MinWidth, fitted.Width);
            Height = Math.Max(MinHeight, fitted.Height);
        }
        Left = fitted.Left;
        Top = fitted.Top;
    }

    private void SaveSettings()
    {
        if (!_placementReady)
        {
            return;
        }
        _settings = _settings with
        {
            Layout = _layout,
            Left = Left,
            Top = Top,
            IsTopmost = Topmost,
            FullWidth = _layout == WidgetLayout.Full ? Math.Clamp(Width, 320, 1600) : _settings.FullWidth,
            FullHeight = _layout == WidgetLayout.Full ? Math.Clamp(Height, 240, 1200) : _settings.FullHeight
        };
        _settingsStore.Save(_settings);
    }
}
