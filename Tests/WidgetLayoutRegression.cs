using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TwseWidget.Controls;
using TwseWidget.Models;
using TwseWidget.Services;
using TwseWidget.ViewModels;

namespace TwseWidget.Tests;

public static class WidgetLayoutRegression
{
    public static bool Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twse-layout-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "ui-settings.json");
        var watchlistPath = Path.Combine(directory, "watchlist.json");
        MainWindow? window = null;
        var success = true;
        void Check(bool value, string description)
        {
            success &= value;
            Console.WriteLine($"{(value ? "PASS" : "FAIL")}: layouts {description}.");
        }

        try
        {
            window = new MainWindow(new WidgetSettingsStore(settingsPath), new WatchlistStore(watchlistPath), false);
            var model = Field<MarketViewModel>(window, "_viewModel");
            var points = Field<List<ChartPoint>>(window, "_chartPoints");
            var watchlist = Field<ObservableCollection<MarketInstrument>>(window, "_watchlist");
            var sample = new MarketInstrument { Code = "00881", Name = "國泰台灣科技龍頭", Exchange = "tse", IsIndex = false };
            watchlist.Add(sample);
            SetField(window, "_selectedInstrument", sample);
            Invoke(window, "SynchronizeWatchlistSelection");
            var start = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(8));
            points.AddRange(Enumerable.Range(0, 271).Select(index =>
                new ChartPoint(start.AddMinutes(index), 52.5m + (decimal)Math.Sin(index / 16d) * 0.2m)));
            var retained = points.ToArray();
            model.SetInstrument(sample);
            model.UpdateClock(start.AddHours(8));
            model.ApplySnapshot(new MarketSnapshot
            {
                InstrumentCode = sample.Code, InstrumentName = sample.Name, IsIndex = false,
                ObservedAt = start.AddMinutes(270), Current = 52.2m, PreviousClose = 51.9m,
                Open = 52.5m, High = 52.8m, Low = 52.15m
            });

            foreach (var (layout, width, height, chartName) in new[]
            {
                (WidgetLayout.Full, 360d, 240d, "ChartA"),
                (WidgetLayout.Mini, 260d, 160d, "ChartMini"),
                (WidgetLayout.Ticker, 280d, 88d, "ChartTicker")
            })
            {
                Invoke(window, "SetLayout", layout, false);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(width, height));
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();
                Check(window.Width == width && window.Height == height, $"{layout} exact logical size");
                Check(!Field<bool>(window, "_isFetching") && Field<int>(window, "_selectionVersion") == 0 &&
                      points.SequenceEqual(retained) && model.ValueText == "52.20", $"{layout} keeps quote/history without fetching");
                foreach (var name in new[] { "WatchlistComboBox", "MiniWatchlistComboBox", "TickerWatchlistComboBox" })
                {
                    var combo = (ComboBox)window.FindName(name);
                    Check(ReferenceEquals(combo.ItemsSource, watchlist) && ReferenceEquals(combo.SelectedItem, sample),
                        $"{layout} shared selection in {name}");
                }
                var chart = (MarketChart)window.FindName(chartName);
                Check(chart.ActualWidth > 30 && chart.ActualHeight > 16, $"{layout} chart has usable space");
                Render(content, width, height, $"layout-{layout}.png");
                // The index's long number must fit without pushing out controls.
                model.SetInstrument(MarketInstrument.Taiex);
                model.ApplySnapshot(new MarketSnapshot
                {
                    IsIndex = true, InstrumentName = MarketInstrument.Taiex.Name,
                    ObservedAt = start.AddMinutes(270), Current = 28512.35m,
                    PreviousClose = 28278.17m, Open = 28200m, High = 28540m, Low = 28150m
                });
                content.UpdateLayout();
                Render(content, width, height, $"layout-{layout}-index.png");
                model.SetInstrument(sample);
                model.ApplySnapshot(new MarketSnapshot
                {
                    InstrumentCode = sample.Code, InstrumentName = sample.Name, IsIndex = false,
                    ObservedAt = start.AddMinutes(270), Current = 52.2m, PreviousClose = 51.9m
                });
            }

            Invoke(window, "SetLayout", WidgetLayout.Full, false);
            var fullContent = (FrameworkElement)window.Content;
            fullContent.Measure(new Size(320, 240));
            fullContent.Arrange(new Rect(0, 0, 320, 240));
            fullContent.UpdateLayout();
            Check(((MarketChart)window.FindName("ChartA")).ActualHeight >= 40,
                "Full minimum size still renders chart");

            var smallScreen = new Rect(0, 0, 1366, 728);
            var fitted = WidgetPlacementPolicy.Fit(new Rect(2000, 1000, 1600, 1200), new[] { smallScreen });
            Check(smallScreen.Contains(fitted) && fitted.Size == smallScreen.Size,
                "oversized restored window fits smaller work area");
            var negativeScreen = new Rect(-1920, 0, 1920, 1040);
            var onSecondScreen = new Rect(-1800, 50, 260, 160);
            Check(WidgetPlacementPolicy.Fit(onSecondScreen, new[] { smallScreen, negativeScreen }) == onSecondScreen,
                "negative-coordinate monitor placement remains unchanged");
            var nearestScreen = WidgetPlacementPolicy.Fit(new Rect(-2500, 200, 260, 160), new[] { smallScreen, negativeScreen });
            Check(negativeScreen.Contains(nearestScreen), "disconnected screen position uses nearest monitor");

            foreach (var presentation in new[] { ChartPresentation.Mini, ChartPresentation.Ticker })
            {
                var chart = new MarketChart { Presentation = presentation };
                chart.Measure(new Size(74, 36));
                chart.Arrange(new Rect(0, 0, 74, 36));
                chart.SetData(new[] { new ChartPoint(start, 52.2m) }, true, true);
                chart.UpdateLayout();
                Render(chart, 74, 36, $"chart-{presentation}-flat.png");
                typeof(MarketChart).GetField("_isPointerOverPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(chart, true);
                typeof(MarketChart).GetField("_pointerX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(chart, 36d);
                chart.SetData(new[] { new ChartPoint(start, 52.2m) }, true, true);
                chart.UpdateLayout();
                var bitmap = new RenderTargetBitmap(74, 36, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(chart);
                var pixels = new byte[74 * 36 * 4];
                bitmap.CopyPixels(pixels, 74 * 4, 0);
                Check(Enumerable.Range(0, 74 * 36).Any(index =>
                    pixels[index * 4 + 2] > 180 && pixels[index * 4 + 1] < 140 && pixels[index * 4] < 140),
                    $"{presentation} flat chart paints red hover guide/marker");
                Check(chart.ToolTip is string text && text.Contains("52.20"), $"{presentation} hover price is available");
                chart.SetData(Array.Empty<ChartPoint>(), false, false);
                chart.UpdateLayout();
                Render(chart, 74, 36, $"chart-{presentation}-empty.png");
            }

            window.Left = 100;
            window.Top = 100;
            SetField(window, "_placementReady", true);
            Invoke(window, "SetLayout", WidgetLayout.Mini, true);
            var stored = new WidgetSettingsStore(settingsPath).Load();
            Check(stored.Layout == WidgetLayout.Mini && stored.Left == 100 && stored.Top == 100,
                "mode and placement persist together");
            window.Close();
            window = new MainWindow(new WidgetSettingsStore(settingsPath), new WatchlistStore(watchlistPath), false);
            Check(window.Width == 260 && window.Height == 160 &&
                  Field<WidgetLayout>(window, "_layout") == WidgetLayout.Mini, "restart restores Mini mode");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: layout regression {exception}");
            success = false;
        }
        finally
        {
            window?.Close();
            foreach (var path in new[] { settingsPath, watchlistPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, false);
            }
        }
        return success;
    }

    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static void Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static void Render(FrameworkElement content, double width, double height, string fileName)
    {
        var image = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var directory = Path.Combine(AppContext.BaseDirectory, "LayoutSnapshots");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }
}
