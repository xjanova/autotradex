using System.Text.Json;
using AutoTradeX.Core.Interfaces;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// CoinGecko API Service - ดึงราคาและข้อมูลเหรียญ
/// Free API: https://api.coingecko.com/api/v3
/// </summary>
public class CoinGeckoService : ICoinDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService _logger;
    private readonly string _baseUrl = "https://api.coingecko.com/api/v3";
    private readonly Dictionary<string, decimal> _priceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;

    public CoinGeckoService(ILoggingService logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AutoTrade-X/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // Initialize common stablecoin prices
        _priceCache["USDT"] = 1m;
        _priceCache["USDC"] = 1m;
        _priceCache["BUSD"] = 1m;
        _priceCache["DAI"] = 1m;
    }

    /// <summary>
    /// ดึงราคาเหรียญหลายตัวพร้อมกัน
    /// </summary>
    public async Task<Dictionary<string, CoinPriceData>> GetPricesAsync(IEnumerable<string> coinIds, string currency = "usd")
    {
        var result = new Dictionary<string, CoinPriceData>();

        try
        {
            var ids = string.Join(",", coinIds);
            var url = $"{_baseUrl}/simple/price?ids={ids}&vs_currencies={currency}&include_24hr_change=true&include_market_cap=true";

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        var price = new CoinPriceData
                        {
                            CoinId = kvp.Key,
                            Price = kvp.Value.TryGetProperty(currency, out var p) ? p.GetDecimal() : 0,
                            Change24h = kvp.Value.TryGetProperty($"{currency}_24h_change", out var c) ? c.GetDecimal() : 0,
                            MarketCap = kvp.Value.TryGetProperty($"{currency}_market_cap", out var m) ? m.GetDecimal() : 0,
                            Currency = currency.ToUpper()
                        };
                        result[kvp.Key] = price;
                    }
                }

                _logger.LogInfo("CoinGecko", $"Fetched prices for {result.Count} coins");
            }
            else
            {
                _logger.LogWarning("CoinGecko", $"API returned {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("CoinGecko", $"Error fetching prices: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// ดึงราคาเหรียญตัวเดียว
    /// </summary>
    public async Task<decimal> GetPriceAsync(string coinId, string currency = "usd")
    {
        var prices = await GetPricesAsync(new[] { coinId }, currency);
        return prices.TryGetValue(coinId, out var price) ? price.Price : 0;
    }

    /// <summary>
    /// ดึงข้อมูลเหรียญรวม Icon URL
    /// </summary>
    public async Task<CoinInfoData?> GetCoinInfoAsync(string coinId)
    {
        try
        {
            var url = $"{_baseUrl}/coins/{coinId}?localization=false&tickers=false&community_data=false&developer_data=false";

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                return new CoinInfoData
                {
                    Id = coinId,
                    Symbol = data.GetProperty("symbol").GetString()?.ToUpper() ?? "",
                    Name = data.GetProperty("name").GetString() ?? "",
                    ImageUrl = data.GetProperty("image").GetProperty("small").GetString() ?? "",
                    ImageUrlLarge = data.GetProperty("image").GetProperty("large").GetString() ?? "",
                    CurrentPrice = data.GetProperty("market_data").GetProperty("current_price").GetProperty("usd").GetDecimal(),
                    MarketCapRank = data.TryGetProperty("market_cap_rank", out var rank) && rank.ValueKind != JsonValueKind.Null ? rank.GetInt32() : 0
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("CoinGecko", $"Error fetching coin info for {coinId}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// ค้นหาเหรียญ
    /// </summary>
    public async Task<List<CoinSearchResultData>> SearchCoinsAsync(string query)
    {
        var results = new List<CoinSearchResultData>();

        try
        {
            var url = $"{_baseUrl}/search?query={Uri.EscapeDataString(query)}";

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.TryGetProperty("coins", out var coins))
                {
                    foreach (var coin in coins.EnumerateArray().Take(20))
                    {
                        results.Add(new CoinSearchResultData
                        {
                            Id = coin.GetProperty("id").GetString() ?? "",
                            Symbol = coin.GetProperty("symbol").GetString()?.ToUpper() ?? "",
                            Name = coin.GetProperty("name").GetString() ?? "",
                            ImageUrl = coin.TryGetProperty("thumb", out var img) ? img.GetString() ?? "" : "",
                            MarketCapRank = coin.TryGetProperty("market_cap_rank", out var rank) && rank.ValueKind != JsonValueKind.Null ? rank.GetInt32() : 0
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("CoinGecko", $"Error searching coins: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// ดึง Top coins by market cap
    /// </summary>
    public async Task<List<CoinMarketDataItem>> GetTopCoinsAsync(int count = 100, string currency = "usd")
    {
        var results = new List<CoinMarketDataItem>();

        try
        {
            var url = $"{_baseUrl}/coins/markets?vs_currency={currency}&order=market_cap_desc&per_page={count}&page=1&sparkline=false";

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var coins = JsonSerializer.Deserialize<List<JsonElement>>(json);

                if (coins != null)
                {
                    foreach (var coin in coins)
                    {
                        results.Add(new CoinMarketDataItem
                        {
                            Id = coin.GetProperty("id").GetString() ?? "",
                            Symbol = coin.GetProperty("symbol").GetString()?.ToUpper() ?? "",
                            Name = coin.GetProperty("name").GetString() ?? "",
                            ImageUrl = coin.GetProperty("image").GetString() ?? "",
                            CurrentPrice = coin.TryGetProperty("current_price", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetDecimal() : 0,
                            MarketCap = coin.TryGetProperty("market_cap", out var mc) && mc.ValueKind != JsonValueKind.Null ? mc.GetDecimal() : 0,
                            MarketCapRank = coin.TryGetProperty("market_cap_rank", out var r) && r.ValueKind != JsonValueKind.Null ? r.GetInt32() : 0,
                            PriceChange24h = coin.TryGetProperty("price_change_percentage_24h", out var pc) && pc.ValueKind != JsonValueKind.Null ? pc.GetDecimal() : 0,
                            Volume24h = coin.TryGetProperty("total_volume", out var v) && v.ValueKind != JsonValueKind.Null ? v.GetDecimal() : 0
                        });
                    }
                }

                _logger.LogInfo("CoinGecko", $"Fetched top {results.Count} coins");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("CoinGecko", $"Error fetching top coins: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Map symbol to CoinGecko ID
    /// </summary>
    public string GetCoinIdFromSymbol(string symbol)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "BTC", "bitcoin" },
            { "ETH", "ethereum" },
            { "USDT", "tether" },
            { "BNB", "binancecoin" },
            { "SOL", "solana" },
            { "XRP", "ripple" },
            { "USDC", "usd-coin" },
            { "ADA", "cardano" },
            { "AVAX", "avalanche-2" },
            { "DOGE", "dogecoin" },
            { "TRX", "tron" },
            { "DOT", "polkadot" },
            { "LINK", "chainlink" },
            { "MATIC", "matic-network" },
            { "TON", "the-open-network" },
            { "SHIB", "shiba-inu" },
            { "LTC", "litecoin" },
            { "BCH", "bitcoin-cash" },
            { "UNI", "uniswap" },
            { "ATOM", "cosmos" },
            { "XLM", "stellar" },
            { "NEAR", "near" },
            { "APT", "aptos" },
            { "ARB", "arbitrum" },
            { "OP", "optimism" },
            { "FIL", "filecoin" },
            { "HBAR", "hedera-hashgraph" },
            { "VET", "vechain" },
            { "ICP", "internet-computer" },
            { "INJ", "injective-protocol" },
            { "AAVE", "aave" },
            { "KUB", "bitkub-coin" },
            { "THB", "thai-baht" }
        };

        return mapping.TryGetValue(symbol.ToUpper(), out var id) ? id : symbol.ToLower();
    }

    /// <summary>
    /// Get cached price for symbol (synchronous)
    /// </summary>
    public decimal GetPrice(string symbol)
    {
        lock (_cacheLock)
        {
            if (_priceCache.TryGetValue(symbol.ToUpperInvariant(), out var price))
            {
                return price;
            }
        }

        // Fallback for common coins if not cached
        return symbol.ToUpperInvariant() switch
        {
            "USDT" or "USDC" or "BUSD" or "DAI" => 1m,
            _ => 0m
        };
    }

    /// <summary>
    /// Refresh price cache for specified symbols
    /// </summary>
    public async Task RefreshPriceCacheAsync(IEnumerable<string> symbols)
    {
        try
        {
            var coinIds = symbols.Select(GetCoinIdFromSymbol).Distinct().ToList();
            var prices = await GetPricesAsync(coinIds);

            lock (_cacheLock)
            {
                foreach (var symbol in symbols)
                {
                    var coinId = GetCoinIdFromSymbol(symbol);
                    if (prices.TryGetValue(coinId, out var priceData))
                    {
                        _priceCache[symbol.ToUpperInvariant()] = priceData.Price;
                    }
                }
                _lastCacheUpdate = DateTime.UtcNow;
            }

            _logger.LogInfo("CoinGecko", $"Price cache updated for {prices.Count} coins");
        }
        catch (Exception ex)
        {
            _logger.LogError("CoinGecko", $"Error refreshing price cache: {ex.Message}");
        }
    }
}
