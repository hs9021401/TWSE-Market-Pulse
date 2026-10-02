using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwseWidget.Models;

namespace TwseWidget.Services;

public sealed class WidgetSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter<WidgetLayout>() }
    };

    private readonly string _filePath;

    public WidgetSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwseWidget",
            "ui-settings.json");
    }

    /// <summary>Returns defaults if the file cannot be read or any setting is invalid.</summary>
    public WidgetSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<WidgetSettings>(
                File.ReadAllText(_filePath), SerializerOptions);
            return IsValid(settings) ? settings! : new WidgetSettings();
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            return new WidgetSettings();
        }
    }

    /// <summary>Persists valid settings, or defaults for invalid input; I/O failures are ignored.</summary>
    public void Save(WidgetSettings settings)
    {
        string? temporaryPath = null;
        var ownsTemporaryFile = false;
        try
        {
            var validatedSettings = IsValid(settings) ? settings : new WidgetSettings();
            var payload = JsonSerializer.SerializeToUtf8Bytes(validatedSettings, SerializerOptions);
            var destination = Path.GetFullPath(_filePath);
            var directory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".ui-settings-{Guid.NewGuid():N}.tmp");

            using (var stream = new FileStream(
                       temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporaryFile = true;
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
            {
                File.Replace(temporaryPath, destination, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, destination);
            }
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            // Settings persistence must not interrupt window movement or shutdown.
        }
        finally
        {
            if (ownsTemporaryFile && temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (IsPersistenceFailure(exception))
                {
                    // Clean up only the temporary file created by this save attempt.
                }
            }
        }
    }

    private static bool IsValid(WidgetSettings? settings) =>
        settings is not null &&
        Enum.IsDefined(settings.Layout) &&
        (settings.Left is null || double.IsFinite(settings.Left.Value)) &&
        (settings.Top is null || double.IsFinite(settings.Top.Value)) &&
        double.IsFinite(settings.FullWidth) &&
        settings.FullWidth >= 320 && settings.FullWidth <= 1600 &&
        double.IsFinite(settings.FullHeight) &&
        settings.FullHeight >= 240 && settings.FullHeight <= 1200;

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or NotSupportedException or SecurityException;
}
