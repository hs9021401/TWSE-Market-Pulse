using System.IO;
using TwseWidget.Models;
using TwseWidget.Services;

namespace TwseWidget.Tests;

public static class WidgetSettingsRegression
{
    public static bool Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twse-widget-settings-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "ui-settings.json");
        var unrelatedPath = Path.Combine(directory, ".ui-settings-unrelated.tmp");
        var blockerPath = Path.Combine(directory, "blocked");
        var success = true;

        void Check(bool condition, string description)
        {
            success &= condition;
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: widget settings {description}.");
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(unrelatedPath, "preserve other files");
            var store = new WidgetSettingsStore(settingsPath);
            Check(store.Load() == new WidgetSettings(), "missing file defaults");

            foreach (var layout in Enum.GetValues<WidgetLayout>())
            {
                var expected = new WidgetSettings
                {
                    Layout = layout,
                    Left = -1920.5,
                    Top = -240.25,
                    FullWidth = 640.5,
                    FullHeight = 480.25,
                    IsTopmost = layout == WidgetLayout.Full
                };
                store.Save(expected);
                Check(new WidgetSettingsStore(settingsPath).Load() == expected,
                    $"{layout} persists including negative coordinates and topmost");
            }

            foreach (var expected in new[]
                     {
                         new WidgetSettings { FullWidth = 320, FullHeight = 240 },
                         new WidgetSettings { FullWidth = 1600, FullHeight = 1200 }
                     })
            {
                store.Save(expected);
                Check(store.Load() == expected, "inclusive size boundary persists");
            }

            var invalidPayloads = new (string Payload, string Description)[]
            {
                ("{broken", "malformed JSON"),
                ("", "empty file"),
                ("null", "null document"),
                ("[]", "wrong document type"),
                ("{\"Layout\":999}", "unknown numeric enum"),
                ("{\"Layout\":\"Unknown\"}", "unknown named enum"),
                ("{\"Layout\":\"Ticker\",\"FullWidth\":319}", "width below minimum"),
                ("{\"Layout\":\"Mini\",\"FullHeight\":239}", "height below minimum"),
                ("{\"FullWidth\":1601}", "width above maximum"),
                ("{\"FullHeight\":1201}", "height above maximum"),
                ("{\"FullWidth\":1e300,\"FullHeight\":1e300}", "huge dimensions"),
                ("{\"FullWidth\":\"NaN\"}", "NaN width"),
                ("{\"FullHeight\":\"NaN\"}", "NaN height"),
                ("{\"Left\":\"NaN\"}", "NaN coordinate"),
                ("{\"Top\":1e400}", "nonfinite coordinate"),
                ("{\"FullWidth\":1e400}", "nonfinite dimension")
            };
            foreach (var (payload, description) in invalidPayloads)
            {
                File.WriteAllText(settingsPath, payload);
                Check(store.Load() == new WidgetSettings(), $"{description} returns defaults");
            }

            File.WriteAllText(settingsPath, "{\"layout\":\"Mini\"}");
            Check(store.Load() == new WidgetSettings { Layout = WidgetLayout.Mini },
                "partial settings retain defaults");

            foreach (var invalid in new[]
                     {
                         new WidgetSettings { Layout = (WidgetLayout)999 },
                         new WidgetSettings { Layout = WidgetLayout.Ticker, FullWidth = double.NaN },
                         new WidgetSettings { Layout = WidgetLayout.Mini, FullHeight = double.NaN },
                         new WidgetSettings { FullWidth = double.PositiveInfinity },
                         new WidgetSettings { FullHeight = double.NegativeInfinity },
                         new WidgetSettings { FullWidth = double.MaxValue, FullHeight = double.MaxValue },
                         new WidgetSettings { FullWidth = 319 },
                         new WidgetSettings { FullHeight = 239 },
                         new WidgetSettings { Left = double.NaN },
                         new WidgetSettings { Top = double.PositiveInfinity }
                     })
            {
                store.Save(invalid);
                Check(store.Load() == new WidgetSettings(), "invalid save persists defaults");
            }

            var retained = new WidgetSettings { Layout = WidgetLayout.Ticker, Left = -50 };
            store.Save(retained);
            using (var lockedFile = new FileStream(
                       settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Check(store.Load() == new WidgetSettings(), "inaccessible file returns defaults");
                store.Save(new WidgetSettings { Layout = WidgetLayout.Mini });
            }
            Check(store.Load() == retained, "failed replacement preserves previous settings");

            File.WriteAllText(blockerPath, "file blocks directory creation");
            var blockedStore = new WidgetSettingsStore(Path.Combine(blockerPath, "ui-settings.json"));
            blockedStore.Save(retained);
            Check(blockedStore.Load() == new WidgetSettings(), "blocked directory fails gracefully");

            var directoryStore = new WidgetSettingsStore(directory);
            directoryStore.Save(retained);
            Check(directoryStore.Load() == new WidgetSettings(), "directory target fails gracefully");

            var invalidPathStore = new WidgetSettingsStore("\0invalid");
            invalidPathStore.Save(retained);
            Check(invalidPathStore.Load() == new WidgetSettings(), "invalid path fails gracefully");
            Check(File.ReadAllText(unrelatedPath) == "preserve other files" &&
                  Directory.GetFiles(directory, ".ui-settings-*.tmp").Length == 1,
                "save cleans up only its own temporary files");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: widget settings regression threw {exception}.");
            success = false;
        }
        finally
        {
            // These exact paths belong to this run. Never recursively delete the folder.
            foreach (var path in new[] { settingsPath, unrelatedPath, blockerPath })
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"FAIL: widget settings cleanup: {exception.Message}");
                    success = false;
                }
            }

            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"FAIL: widget settings folder cleanup: {exception.Message}");
                success = false;
            }
        }

        return success;
    }
}
