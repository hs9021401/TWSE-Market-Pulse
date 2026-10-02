using System.IO;
using System.Linq;
using System.Text.Json;
using TwseWidget.Models;

namespace TwseWidget.Services;

/// <summary>
/// Persists the user's stock watchlist independently from the widget process.
/// </summary>
public sealed class WatchlistStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public WatchlistStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwseWidget",
            "watchlist.json");
    }

    public IReadOnlyList<MarketInstrument> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<MarketInstrument>();
            }

            var entries = JsonSerializer.Deserialize<List<WatchlistEntry>>(
                File.ReadAllText(_filePath),
                SerializerOptions);

            return entries is null
                ? Array.Empty<MarketInstrument>()
                : entries
                    .Where(IsValid)
                    .GroupBy(entry => $"{entry.Exchange}:{entry.Code}", StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Select(entry => new MarketInstrument
                    {
                        Code = entry.Code.Trim(),
                        Name = entry.Name.Trim(),
                        Exchange = entry.Exchange.Trim().ToLowerInvariant(),
                        IsIndex = false
                    })
                    .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<MarketInstrument>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<MarketInstrument>();
        }
        catch (JsonException)
        {
            return Array.Empty<MarketInstrument>();
        }
    }

    public void Save(IEnumerable<MarketInstrument> instruments)
    {
        try
        {
            var entries = instruments
                .Where(instrument => !instrument.IsIndex)
                .Select(instrument => new WatchlistEntry
                {
                    Code = instrument.Code.Trim(),
                    Name = instrument.Name.Trim(),
                    Exchange = instrument.Exchange.Trim().ToLowerInvariant()
                })
                .Where(IsValid)
                .GroupBy(entry => $"{entry.Exchange}:{entry.Code}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                _filePath,
                JsonSerializer.Serialize(entries, SerializerOptions));
        }
        catch (IOException)
        {
            // A persistence failure must not prevent the market widget from closing.
        }
        catch (UnauthorizedAccessException)
        {
            // A persistence failure must not prevent the market widget from closing.
        }
    }

    private static bool IsValid(WatchlistEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Code) &&
        !string.IsNullOrWhiteSpace(entry.Name) &&
        (entry.Exchange.Equals("tse", StringComparison.OrdinalIgnoreCase) ||
         entry.Exchange.Equals("otc", StringComparison.OrdinalIgnoreCase));

    private sealed class WatchlistEntry
    {
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Exchange { get; set; } = "tse";
    }
}
