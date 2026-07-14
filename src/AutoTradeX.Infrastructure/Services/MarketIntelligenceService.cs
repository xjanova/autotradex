/*
 * ============================================================================
 * AutoTrade-X - Market Intelligence Service
 * ============================================================================
 * Fetches real external market data from trusted free sources:
 *  - Crypto news headlines via RSS (CoinDesk, Cointelegraph, Decrypt)
 *  - Fear & Greed Index (alternative.me)
 *  - Historical coin statistics (CoinGecko 30-day market chart)
 * Sentiment scoring itself is pure logic in Core (SentimentAnalyzer).
 *
 * Design rules (per project audit notes):
 *  - HttpClient created ONCE in constructor (no per-call clients)
 *  - Every fetch is cached and failure-tolerant: methods return null,
 *    never throw, so the trading loop is never blocked by intel outages
 * ============================================================================
 */

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Core.Services;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// บริการข้อมูลอัจฉริยะตลาด: ข่าว + Fear/Greed + สถิติย้อนหลัง
/// </summary>
public class MarketIntelligenceService : IMarketIntelligenceService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService _logger;

    // Caches — intel data changes slowly; avoid hammering free APIs
    private FearGreedData? _fearGreedCache;
    private DateTime _fearGreedFetchedAt = DateTime.MinValue;
    private static readonly TimeSpan FearGreedTtl = TimeSpan.FromMinutes(30);

    private List<NewsArticle>? _rawNewsCache; // market-wide, before coin filtering
    private DateTime _newsFetchedAt = DateTime.MinValue;
    private static readonly TimeSpan NewsTtl = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, (CoinHistoricalStats stats, DateTime fetchedAt)> _statsCache
        = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(30);

    // Serialize fetches so concurrent callers don't stampede the APIs
    private readonly SemaphoreSlim _fearGreedLock = new(1, 1);
    private readonly SemaphoreSlim _newsLock = new(1, 1);
    private readonly SemaphoreSlim _statsLock = new(1, 1);

    private static readonly (string Name, string Url)[] NewsFeeds =
    {
        ("CoinDesk", "https://www.coindesk.com/arc/outboundfeeds/rss/"),
        ("Cointelegraph", "https://cointelegraph.com/rss"),
        ("Decrypt", "https://decrypt.co/feed"),
    };

    // Symbol → CoinGecko id for common trading pairs
    private static readonly Dictionary<string, string> CoinGeckoIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "bitcoin", ["ETH"] = "ethereum", ["BNB"] = "binancecoin",
        ["SOL"] = "solana", ["XRP"] = "ripple", ["ADA"] = "cardano",
        ["DOGE"] = "dogecoin", ["DOT"] = "polkadot", ["MATIC"] = "matic-network",
        ["POL"] = "polygon-ecosystem-token", ["AVAX"] = "avalanche-2",
        ["LINK"] = "chainlink", ["UNI"] = "uniswap", ["LTC"] = "litecoin",
        ["ATOM"] = "cosmos", ["NEAR"] = "near", ["APT"] = "aptos",
        ["ARB"] = "arbitrum", ["OP"] = "optimism", ["SHIB"] = "shiba-inu",
        ["PEPE"] = "pepe", ["SUI"] = "sui", ["TON"] = "the-open-network",
        ["TRX"] = "tron", ["XLM"] = "stellar", ["FIL"] = "filecoin",
        ["ICP"] = "internet-computer", ["INJ"] = "injective-protocol",
        ["KUB"] = "bitkub-coin", ["ETC"] = "ethereum-classic",
        ["BCH"] = "bitcoin-cash", ["AAVE"] = "aave", ["ALGO"] = "algorand",
        ["SAND"] = "the-sandbox", ["MANA"] = "decentraland", ["GALA"] = "gala",
        ["FTM"] = "fantom", ["HBAR"] = "hedera-hashgraph", ["VET"] = "vechain",
        ["EOS"] = "eos", ["XTZ"] = "tezos", ["THETA"] = "theta-token",
        ["AXS"] = "axie-infinity", ["CHZ"] = "chiliz", ["GRT"] = "the-graph",
        ["ENJ"] = "enjincoin", ["ZIL"] = "zilliqa", ["KSM"] = "kusama",
        ["FLOW"] = "flow", ["CRV"] = "curve-dao-token",
    };

    public MarketIntelligenceService(ILoggingService logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AutoTrade-X/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, application/rss+xml, application/xml, text/xml");
    }

    public async Task<MarketIntelligence?> GetIntelligenceAsync(string baseAsset, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseAsset)) return null;

        try
        {
            // Fetch all three sources concurrently — each is independently cached and failure-tolerant
            var fearGreedTask = GetFearGreedIndexAsync(cancellationToken);
            var newsTask = GetNewsSentimentAsync(baseAsset, cancellationToken);
            var statsTask = GetCoinStatsAsync(baseAsset, cancellationToken);

            await Task.WhenAll(fearGreedTask, newsTask, statsTask);

            var intel = new MarketIntelligence
            {
                BaseAsset = baseAsset.ToUpperInvariant(),
                FearGreed = fearGreedTask.Result,
                News = newsTask.Result,
                Stats = statsTask.Result,
                FetchedAt = DateTime.UtcNow
            };

            if (!intel.HasAnyData) return null;

            intel.CompositeScore = ComputeCompositeScore(intel);
            intel.Summary = BuildSummary(intel);
            return intel;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MarketIntel", $"GetIntelligenceAsync failed: {ex.Message}");
            return null;
        }
    }

    public async Task<FearGreedData?> GetFearGreedIndexAsync(CancellationToken cancellationToken = default)
    {
        if (_fearGreedCache != null && DateTime.UtcNow - _fearGreedFetchedAt < FearGreedTtl)
            return _fearGreedCache;

        await _fearGreedLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check after acquiring the lock — another caller may have refreshed it
            if (_fearGreedCache != null && DateTime.UtcNow - _fearGreedFetchedAt < FearGreedTtl)
                return _fearGreedCache;

            var json = await _httpClient.GetStringAsync("https://api.alternative.me/fng/?limit=8&format=json", cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                return _fearGreedCache;

            var entries = new List<(int value, string classification)>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("value", out var v) &&
                    int.TryParse(v.GetString(), out var value))
                {
                    var cls = item.TryGetProperty("value_classification", out var c)
                        ? c.GetString() ?? "" : "";
                    entries.Add((value, cls));
                }
            }

            if (entries.Count == 0) return _fearGreedCache;

            _fearGreedCache = new FearGreedData
            {
                Value = entries[0].value,
                Classification = entries[0].classification,
                YesterdayValue = entries.Count > 1 ? entries[1].value : null,
                WeekAverage = entries.Count >= 7 ? (decimal)entries.Take(7).Average(e => e.value) : null,
                Timestamp = DateTime.UtcNow
            };
            _fearGreedFetchedAt = DateTime.UtcNow;

            _logger.LogInfo("MarketIntel", $"Fear & Greed: {_fearGreedCache.Value} ({_fearGreedCache.Classification})");
            return _fearGreedCache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MarketIntel", $"Fear & Greed fetch failed: {ex.Message}");
            return _fearGreedCache; // stale is better than nothing
        }
        finally
        {
            _fearGreedLock.Release();
        }
    }

    public async Task<NewsSentimentSummary?> GetNewsSentimentAsync(string baseAsset, CancellationToken cancellationToken = default)
    {
        var articles = await GetRawNewsAsync(cancellationToken);
        if (articles == null || articles.Count == 0) return null;

        // Coin-specific tagging is cheap — do it per call on the shared cache
        var tagged = articles.Select(a => new NewsArticle
        {
            Title = a.Title,
            Source = a.Source,
            Url = a.Url,
            PublishedAt = a.PublishedAt,
            SentimentScore = a.SentimentScore,
            MatchedKeywords = a.MatchedKeywords,
            IsCoinSpecific = SentimentAnalyzer.IsAboutCoin(a.Title, baseAsset)
        }).ToList();

        return SentimentAnalyzer.Summarize(baseAsset.ToUpperInvariant(), tagged);
    }

    private async Task<List<NewsArticle>?> GetRawNewsAsync(CancellationToken cancellationToken)
    {
        if (_rawNewsCache != null && DateTime.UtcNow - _newsFetchedAt < NewsTtl)
            return _rawNewsCache;

        await _newsLock.WaitAsync(cancellationToken);
        try
        {
            if (_rawNewsCache != null && DateTime.UtcNow - _newsFetchedAt < NewsTtl)
                return _rawNewsCache;

            var fetchTasks = NewsFeeds.Select(feed => FetchFeedAsync(feed.Name, feed.Url, cancellationToken));
            var results = await Task.WhenAll(fetchTasks);

            var all = results.SelectMany(r => r)
                .Where(a => (DateTime.UtcNow - a.PublishedAt).TotalHours <= 48)
                .GroupBy(a => a.Title, StringComparer.OrdinalIgnoreCase) // dedupe cross-feed reposts
                .Select(g => g.First())
                .OrderByDescending(a => a.PublishedAt)
                .Take(60)
                .ToList();

            if (all.Count > 0)
            {
                _rawNewsCache = all;
                _newsFetchedAt = DateTime.UtcNow;
                _logger.LogInfo("MarketIntel", $"Fetched {all.Count} news headlines from {NewsFeeds.Length} feeds");
            }

            return _rawNewsCache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MarketIntel", $"News fetch failed: {ex.Message}");
            return _rawNewsCache;
        }
        finally
        {
            _newsLock.Release();
        }
    }

    private async Task<List<NewsArticle>> FetchFeedAsync(string sourceName, string url, CancellationToken cancellationToken)
    {
        var articles = new List<NewsArticle>();
        try
        {
            var xml = await _httpClient.GetStringAsync(url, cancellationToken);
            var doc = XDocument.Parse(xml);

            // RSS 2.0: rss/channel/item; Atom: feed/entry
            var items = doc.Descendants("item").ToList();
            if (items.Count == 0)
            {
                XNamespace atom = "http://www.w3.org/2005/Atom";
                items = doc.Descendants(atom + "entry").ToList();
            }

            foreach (var item in items.Take(30))
            {
                var title = item.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(title)) continue;

                var link = item.Elements().FirstOrDefault(e => e.Name.LocalName == "link")?.Value?.Trim()
                    ?? item.Elements().FirstOrDefault(e => e.Name.LocalName == "link")?.Attribute("href")?.Value
                    ?? "";

                var dateText = item.Elements().FirstOrDefault(e =>
                    e.Name.LocalName is "pubDate" or "published" or "updated" or "date")?.Value?.Trim();
                var published = ParseFeedDate(dateText);

                var (score, matched) = SentimentAnalyzer.ScoreHeadline(title);
                articles.Add(new NewsArticle
                {
                    Title = title,
                    Source = sourceName,
                    Url = link,
                    PublishedAt = published,
                    SentimentScore = score,
                    MatchedKeywords = matched
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MarketIntel", $"Feed {sourceName} failed: {ex.Message}");
        }
        return articles;
    }

    private static DateTime ParseFeedDate(string? dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText)) return DateTime.UtcNow;

        if (DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var dto))
            return dto.UtcDateTime;

        // RFC-1123-ish with nonstandard zone suffixes (e.g. "GMT", "EST")
        var trimmed = dateText.Length > 25 ? dateText[..25].Trim() : dateText;
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dto))
            return dto.UtcDateTime;

        return DateTime.UtcNow;
    }

    public async Task<CoinHistoricalStats?> GetCoinStatsAsync(string baseAsset, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseAsset)) return null;
        var asset = baseAsset.ToUpperInvariant();

        lock (_statsCache)
        {
            if (_statsCache.TryGetValue(asset, out var cached) &&
                DateTime.UtcNow - cached.fetchedAt < StatsTtl)
                return cached.stats;
        }

        await _statsLock.WaitAsync(cancellationToken);
        try
        {
            lock (_statsCache)
            {
                if (_statsCache.TryGetValue(asset, out var cached) &&
                    DateTime.UtcNow - cached.fetchedAt < StatsTtl)
                    return cached.stats;
            }

            var coinId = CoinGeckoIds.TryGetValue(asset, out var id) ? id : asset.ToLowerInvariant();

            var stats = new CoinHistoricalStats
            {
                BaseAsset = asset,
                CoinGeckoId = coinId,
                FetchedAt = DateTime.UtcNow
            };

            // 1) Market snapshot: rank, ATH distance, volume/mcap
            var marketsUrl = $"https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&ids={coinId}" +
                             "&price_change_percentage=7d,30d";
            var marketsJson = await _httpClient.GetStringAsync(marketsUrl, cancellationToken);
            using (var doc = JsonDocument.Parse(marketsJson))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                {
                    _logger.LogWarning("MarketIntel", $"CoinGecko: no market data for '{coinId}'");
                    return null;
                }

                var coin = doc.RootElement[0];
                stats.Name = GetString(coin, "name");
                stats.MarketCapRank = GetInt(coin, "market_cap_rank");
                stats.AthDistancePercent = GetDecimal(coin, "ath_change_percentage");
                stats.PriceChange7d = GetDecimal(coin, "price_change_percentage_7d_in_currency");
                stats.PriceChange30d = GetDecimal(coin, "price_change_percentage_30d_in_currency");

                var marketCap = GetDecimal(coin, "market_cap");
                var volume = GetDecimal(coin, "total_volume");
                stats.VolumeToMarketCap = marketCap > 0 ? volume / marketCap : 0;
            }

            // 2) 30-day daily prices → volatility, max drawdown, Sharpe, hi/lo
            var chartUrl = $"https://api.coingecko.com/api/v3/coins/{coinId}/market_chart?vs_currency=usd&days=30&interval=daily";
            var chartJson = await _httpClient.GetStringAsync(chartUrl, cancellationToken);
            using (var doc = JsonDocument.Parse(chartJson))
            {
                if (doc.RootElement.TryGetProperty("prices", out var pricesEl))
                {
                    var prices = new List<decimal>();
                    foreach (var point in pricesEl.EnumerateArray())
                    {
                        if (point.GetArrayLength() >= 2)
                            prices.Add(point[1].GetDecimal());
                    }

                    if (prices.Count >= 8)
                    {
                        ComputePriceStatistics(prices, stats);
                    }
                }
            }

            lock (_statsCache)
            {
                _statsCache[asset] = (stats, DateTime.UtcNow);
                // Bound the cache — one entry per traded asset is small, but be safe
                if (_statsCache.Count > 50)
                {
                    var oldest = _statsCache.OrderBy(kvp => kvp.Value.fetchedAt).First().Key;
                    _statsCache.Remove(oldest);
                }
            }

            _logger.LogInfo("MarketIntel",
                $"Stats {asset}: rank #{stats.MarketCapRank}, 30d {stats.PriceChange30d:F1}%, vol {stats.DailyVolatility30d:F2}%/d, DD {stats.MaxDrawdown30d:F1}%");
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MarketIntel", $"Coin stats fetch failed for {asset}: {ex.Message}");
            return null;
        }
        finally
        {
            _statsLock.Release();
        }
    }

    /// <summary>คำนวณ volatility / drawdown / sharpe / hi-lo จากราคารายวัน 30 วัน</summary>
    private static void ComputePriceStatistics(List<decimal> prices, CoinHistoricalStats stats)
    {
        stats.High30d = prices.Max();
        stats.Low30d = prices.Min();

        // Daily returns (%)
        var returns = new List<double>();
        for (int i = 1; i < prices.Count; i++)
        {
            if (prices[i - 1] > 0)
                returns.Add((double)((prices[i] - prices[i - 1]) / prices[i - 1]) * 100);
        }

        if (returns.Count > 1)
        {
            var mean = returns.Average();
            var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
            var stdDev = Math.Sqrt(variance);
            stats.DailyVolatility30d = (decimal)stdDev;
            // Annualized Sharpe (rf = 0): mean/std × √365
            stats.SharpeRatio30d = stdDev > 0 ? (decimal)(mean / stdDev * Math.Sqrt(365)) : 0;
        }

        // Max drawdown over the window
        decimal peak = prices[0], maxDd = 0;
        foreach (var price in prices)
        {
            if (price > peak) peak = price;
            if (peak > 0)
            {
                var dd = (peak - price) / peak * 100;
                if (dd > maxDd) maxDd = dd;
            }
        }
        stats.MaxDrawdown30d = maxDd;
    }

    /// <summary>
    /// คะแนนรวม -100..+100: ข่าว 50% + Fear/Greed mood 30% + โมเมนตัม 7 วัน 20%
    /// </summary>
    private static int ComputeCompositeScore(MarketIntelligence intel)
    {
        decimal score = 0, weightSum = 0;

        if (intel.News != null)
        {
            score += intel.News.EffectiveScore * 0.5m;
            weightSum += 0.5m;
        }

        if (intel.FearGreed != null)
        {
            // 50 = neutral → 0; 0 = extreme fear → -100; 100 = extreme greed → +100
            score += (intel.FearGreed.Value - 50) * 2 * 0.3m;
            weightSum += 0.3m;
        }

        if (intel.Stats != null)
        {
            score += Math.Clamp(intel.Stats.PriceChange7d * 5, -100, 100) * 0.2m;
            weightSum += 0.2m;
        }

        return weightSum > 0 ? Math.Clamp((int)(score / weightSum), -100, 100) : 0;
    }

    private static string BuildSummary(MarketIntelligence intel)
    {
        var parts = new List<string>();

        if (intel.News != null)
        {
            var mood = intel.News.EffectiveScore switch
            {
                >= 20 => "ข่าวเชิงบวก",
                <= -20 => "ข่าวเชิงลบ",
                _ => "ข่าวเป็นกลาง"
            };
            parts.Add($"{mood} ({intel.News.EffectiveScore:+0;-0;0}) จาก {intel.News.Articles.Count} ข่าว");
        }

        if (intel.FearGreed != null)
            parts.Add($"Fear&Greed {intel.FearGreed.Value} ({intel.FearGreed.Classification})");

        if (intel.Stats != null)
            parts.Add($"30วัน {intel.Stats.PriceChange30d:+0.0;-0.0}% | ผันผวน {intel.Stats.DailyVolatility30d:F1}%/วัน");

        return string.Join(" | ", parts);
    }

    #region JSON helpers

    private static string GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;

    private static decimal GetDecimal(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal() : 0;

    #endregion

    public void Dispose()
    {
        _httpClient.Dispose();
        _fearGreedLock.Dispose();
        _newsLock.Dispose();
        _statsLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
