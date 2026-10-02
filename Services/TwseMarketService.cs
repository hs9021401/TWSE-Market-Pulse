using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using TwseWidget.Models;

namespace TwseWidget.Services;

public sealed class TwseMarketService : IDisposable
{
    private const string BaseApiUrl = "https://mis.twse.com.tw/stock/api/";
    private const string YahooChartApiUrl = "https://query1.finance.yahoo.com/v8/finance/chart/";

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, MarketSnapshot> _lastSnapshots =
        new(StringComparer.OrdinalIgnoreCase);

    public TwseMarketService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("TwseWidget", "0.2"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        _httpClient.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
    }

    public Task<MarketSnapshot> GetLatestAsync(CancellationToken cancellationToken = default) =>
        GetLatestAsync(MarketInstrument.Taiex, cancellationToken);

    public async Task<MarketSnapshot> GetLatestAsync(
        MarketInstrument instrument,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri(
            $"{BaseApiUrl}getStockInfo.jsp?ex_ch={Uri.EscapeDataString(instrument.QuoteChannel)}&json=1&delay=0&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        using var response = await _httpClient.GetAsync(
            endpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("msgArray", out var messages) ||
            messages.ValueKind != JsonValueKind.Array ||
            messages.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"TWSE 沒有回傳 {instrument.DisplayText} 的行情資料。");
        }

        var item = messages[0];
        var liveCurrent = ReadDecimal(item, "z");
        var hasPreviousSnapshot = _lastSnapshots.TryGetValue(
            instrument.Key,
            out var previousSnapshot);
        var current = liveCurrent ??
                      ReadDecimal(item, "pz") ??
                      (hasPreviousSnapshot ? previousSnapshot!.Current : (decimal?)null) ??
                      ReadDecimal(item, "y");
        if (current is null)
        {
            throw new InvalidOperationException($"TWSE 回傳的 {instrument.DisplayText} 欄位尚未 ready。");
        }

        var previousClose = ReadDecimal(item, "y") ?? current.Value;
        var open = ReadDecimal(item, "o") ?? current.Value;
        var high = ReadDecimal(item, "h") ?? current.Value;
        var low = ReadDecimal(item, "l") ?? current.Value;
        var date = ReadString(item, "d");
        var time = ReadString(item, "t") ?? ReadString(item, "%") ?? "--:--:--";
        var displayName = ReadString(item, "n") ?? instrument.Name;

        var snapshot = new MarketSnapshot
        {
            InstrumentCode = instrument.Code,
            InstrumentName = displayName,
            Exchange = instrument.Exchange,
            IsIndex = instrument.IsIndex,
            IsLive = liveCurrent is not null,
            ObservedAt = TaiwanMarketClock.BuildMarketDateTime(date, time),
            FetchedAt = TaiwanMarketClock.Now,
            Current = current.Value,
            PreviousClose = previousClose,
            Open = open,
            High = high,
            Low = low,
            MarketTime = time,
            Source = $"TWSE MIS / {instrument.QuoteChannel}"
        };

        _lastSnapshots[instrument.Key] = snapshot;
        return snapshot;
    }

    public async Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0)
        {
            return Array.Empty<StockSearchResult>();
        }

        var endpoint = new Uri(
            $"{BaseApiUrl}getStockNames.jsp?n={Uri.EscapeDataString(normalizedQuery)}&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        using var response = await _httpClient.GetAsync(
            endpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("datas", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<StockSearchResult>();
        }

        var candidates = data.EnumerateArray()
            .Select(item =>
            {
                var code = ReadString(item, "c");
                var name = ReadString(item, "n");
                var key = ReadString(item, "key");
                var exchange = key?.StartsWith("otc_", StringComparison.OrdinalIgnoreCase) == true
                    ? "otc"
                    : "tse";

                return new StockSearchResult
                {
                    Code = code ?? string.Empty,
                    Name = name ?? string.Empty,
                    Exchange = exchange
                };
            });

        return FilterSearchResults(candidates, normalizedQuery);
    }

    internal static IReadOnlyList<StockSearchResult> FilterSearchResults(
        IEnumerable<StockSearchResult> candidates,
        string normalizedQuery)
    {
        return candidates
            .Where(item => IsSearchableSecurity(item) &&
                           (item.Code.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                            item.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => MatchRank(item, normalizedQuery))
            .ThenBy(SearchCategoryRank)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    public Task<IReadOnlyList<ChartPoint>> GetIntradayHistoryAsync(
        DateTimeOffset sessionDate,
        CancellationToken cancellationToken = default) =>
        GetIntradayHistoryAsync(MarketInstrument.Taiex, sessionDate, cancellationToken);

    public async Task<IReadOnlyList<ChartPoint>> GetIntradayHistoryAsync(
        MarketInstrument instrument,
        DateTimeOffset sessionDate,
        CancellationToken cancellationToken = default)
    {
        if (!instrument.IsIndex)
        {
            // TWSE MIS exposes the live quote for individual stocks, but its public
            // chart endpoint currently returns only the index series. Yahoo's public
            // one-minute chart feed fills the pre-selection portion of the MVP chart.
            var suffix = instrument.Exchange.Equals("otc", StringComparison.OrdinalIgnoreCase)
                ? "TWO"
                : "TW";
            return await GetYahooIntradayHistoryAsync(
                $"{instrument.Code}.{suffix}",
                sessionDate,
                cancellationToken);
        }

        try
        {
            var indexEndpoint = new Uri(
                $"{BaseApiUrl}getChartOhlcStatis.jsp?ex={instrument.Exchange}&ch={Uri.EscapeDataString(instrument.Channel)}&fqy=1&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            using var response = await _httpClient.GetAsync(
                indexEndpoint,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var history = TwseMarketPayloadParser.ParseIntradayHistory(payload, sessionDate);
            var historyLooksCompleteAfterClose =
                TaiwanMarketClock.GetSession(TaiwanMarketClock.Now) != MarketSession.PostClose ||
                history.Count >= 3;
            if (history.Count > 0 && historyLooksCompleteAfterClose)
            {
                return history;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout: use the independent history fallback below.
        }
        catch (HttpRequestException)
        {
            // A transient TWSE chart failure should not erase an already complete
            // chart. Try the independent one-minute history feed below.
        }
        catch (JsonException)
        {
            // Treat a malformed/empty TWSE chart payload like a transient failure.
        }

        // The TWSE chart endpoint is the primary source for TAIEX. It has returned
        // empty payloads intermittently, including after the market closes. Yahoo's
        // ^TWII one-minute feed is only a chart-history fallback; live quote values
        // remain sourced from TWSE MIS in GetLatestAsync.
        return await GetYahooIntradayHistoryAsync(
            "^TWII",
            sessionDate,
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<IReadOnlyList<ChartPoint>> GetYahooIntradayHistoryAsync(
        string symbol,
        DateTimeOffset sessionDate,
        CancellationToken cancellationToken)
    {
        var yahooEndpoint = new Uri(
            $"{YahooChartApiUrl}{Uri.EscapeDataString(symbol)}?range=1d&interval=1m&includePrePost=false&events=div%2Csplits&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        using var yahooResponse = await _httpClient.GetAsync(
            yahooEndpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        yahooResponse.EnsureSuccessStatusCode();

        var yahooPayload = await yahooResponse.Content.ReadAsStringAsync(cancellationToken);
        return YahooMarketPayloadParser.ParseIntradayHistory(yahooPayload, sessionDate);
    }

    private static bool IsSearchableSecurity(StockSearchResult item)
    {
        // TWSE uses four-digit stock codes, five-digit ETF codes, and also
        // six-character alphanumeric codes such as 00403A.
        if (item.Code.Length < 4 ||
            item.Code.Length > 6 ||
            !item.Code.All(char.IsLetterOrDigit))
        {
            return false;
        }

        return !item.Name.Contains('購') &&
               !item.Name.Contains('售') &&
               !item.Name.Contains('牛') &&
               !item.Name.Contains('熊') &&
               !item.Name.Contains("權證", StringComparison.Ordinal);
    }

    private static int MatchRank(StockSearchResult item, string query)
    {
        if (item.Code.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (item.Code.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static int SearchCategoryRank(StockSearchResult item) =>
        item.Code.All(char.IsDigit) ? 0 : 1;

    private static string? ReadString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement item, string propertyName)
    {
        var value = ReadString(item, propertyName);
        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return null;
        }

        return decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }
}
