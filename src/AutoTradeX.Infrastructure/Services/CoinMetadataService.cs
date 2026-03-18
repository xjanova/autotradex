/*
 * ============================================================================
 * AutoTrade-X - Coin Metadata Service
 * ============================================================================
 * Fetches and caches coin icons and metadata from CoinGecko API
 * Stores icon data in SQLite for offline access
 * ============================================================================
 */

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Infrastructure.Data;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// Interface for coin metadata service
/// </summary>
public interface ICoinMetadataService
{
    /// <summary>
    /// Get coin icon as byte array (from cache or fetch from API)
    /// </summary>
    Task<byte[]?> GetCoinIconAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get coin metadata
    /// </summary>
    Task<CoinMetadata?> GetCoinMetadataAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pre-cache icons for multiple coins (called during startup)
    /// </summary>
    Task PreCacheIconsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if icon is cached
    /// </summary>
    Task<bool> HasCachedIconAsync(string symbol);
}

/// <summary>
/// Coin metadata model
/// </summary>
public class CoinMetadata
{
    public string Symbol { get; set; } = "";
    public string? Name { get; set; }
    public string? IconUrl { get; set; }
    public byte[]? IconData { get; set; }
    public string? CoinGeckoId { get; set; }
    public int? MarketCapRank { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Service for fetching and caching coin metadata
/// </summary>
public class CoinMetadataService : ICoinMetadataService
{
    private readonly IDatabaseService _database;
    private readonly ILoggingService _logger;
    private readonly HttpClient _httpClient;

    // In-memory cache for quick access
    private readonly Dictionary<string, byte[]> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    // CoinGecko API (free, no API key required for basic usage)
    private const string CoinGeckoApiBase = "https://api.coingecko.com/api/v3";

    // Common symbol to CoinGecko ID mapping
    private static readonly Dictionary<string, string> SymbolToGeckoId = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BTC", "bitcoin" }, { "ETH", "ethereum" }, { "USDT", "tether" },
        { "BNB", "binancecoin" }, { "SOL", "solana" }, { "XRP", "ripple" },
        { "USDC", "usd-coin" }, { "ADA", "cardano" }, { "AVAX", "avalanche-2" },
        { "DOGE", "dogecoin" }, { "DOT", "polkadot" }, { "LINK", "chainlink" },
        { "MATIC", "matic-network" }, { "SHIB", "shiba-inu" }, { "LTC", "litecoin" },
        { "TRX", "tron" }, { "UNI", "uniswap" }, { "ATOM", "cosmos" },
        { "XLM", "stellar" }, { "NEAR", "near" }, { "APT", "aptos" },
        { "ARB", "arbitrum" }, { "OP", "optimism" }, { "AAVE", "aave" },
        { "TON", "the-open-network" }, { "PEPE", "pepe" }, { "SUI", "sui" },
        { "SEI", "sei-network" }, { "INJ", "injective-protocol" }, { "TIA", "celestia" },
        { "FET", "fetch-ai" }, { "TAO", "bittensor" }, { "RENDER", "render-token" },
        { "WIF", "dogwifcoin" }, { "JUP", "jupiter-exchange-solana" }, { "PYTH", "pyth-network" },
        { "STX", "stacks" }, { "RUNE", "thorchain" }, { "FIL", "filecoin" },
        { "IMX", "immutable-x" }, { "BCH", "bitcoin-cash" }, { "ETC", "ethereum-classic" },
        { "SAND", "the-sandbox" }, { "MANA", "decentraland" }, { "CRV", "curve-dao-token" },
        { "GMT", "stepn" }, { "APE", "apecoin" }, { "FTM", "fantom" },
        { "GRT", "the-graph" }, { "ALGO", "algorand" }, { "XTZ", "tezos" },
        { "EOS", "eos" }, { "THETA", "theta-token" }, { "VET", "vechain" },
        { "HBAR", "hedera-hashgraph" }, { "FLOW", "flow" }, { "AXS", "axie-infinity" },
        { "KCS", "kucoin-shares" }, { "1INCH", "1inch" }, { "SNX", "synthetix-network-token" }
    };

    public CoinMetadataService(IDatabaseService database, ILoggingService logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AutoTradeX/1.0");
    }

    public async Task<byte[]?> GetCoinIconAsync(string symbol, CancellationToken cancellationToken = default)
    {
        symbol = NormalizeSymbol(symbol);

        // Check in-memory cache first
        if (_iconCache.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double check after acquiring lock
            if (_iconCache.TryGetValue(symbol, out cached))
            {
                return cached;
            }

            // Check database cache
            var metadata = await GetFromDatabaseAsync(symbol);
            if (metadata?.IconData != null && metadata.IconData.Length > 0)
            {
                _iconCache[symbol] = metadata.IconData;
                return metadata.IconData;
            }

            // Fetch from API
            var iconData = await FetchIconFromApiAsync(symbol, cancellationToken);
            if (iconData != null && iconData.Length > 0)
            {
                _iconCache[symbol] = iconData;
                await SaveToDatabaseAsync(symbol, iconData);
                return iconData;
            }

            return null;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<CoinMetadata?> GetCoinMetadataAsync(string symbol, CancellationToken cancellationToken = default)
    {
        symbol = NormalizeSymbol(symbol);
        return await GetFromDatabaseAsync(symbol);
    }

    public async Task<bool> HasCachedIconAsync(string symbol)
    {
        symbol = NormalizeSymbol(symbol);

        if (_iconCache.ContainsKey(symbol))
            return true;

        var metadata = await GetFromDatabaseAsync(symbol);
        return metadata?.IconData != null && metadata.IconData.Length > 0;
    }

    public async Task PreCacheIconsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("CoinMetadata", $"Pre-caching icons for {symbols.Count()} symbols...");

        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(5); // Limit concurrent requests

        foreach (var symbol in symbols.Distinct())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await semaphore.WaitAsync(cancellationToken);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await GetCoinIconAsync(symbol, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("CoinMetadata", $"Failed to cache icon for {symbol}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        _logger.LogInfo("CoinMetadata", $"Pre-cached {_iconCache.Count} icons");
    }

    private async Task<byte[]?> FetchIconFromApiAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // Get CoinGecko ID
            if (!SymbolToGeckoId.TryGetValue(symbol, out var geckoId))
            {
                // Try to search for the coin
                geckoId = await SearchCoinGeckoIdAsync(symbol, cancellationToken);
                if (string.IsNullOrEmpty(geckoId))
                {
                    _logger.LogWarning("CoinMetadata", $"Could not find CoinGecko ID for {symbol}");
                    return null;
                }
            }

            // Get coin data from CoinGecko
            var response = await _httpClient.GetAsync(
                $"{CoinGeckoApiBase}/coins/{geckoId}?localization=false&tickers=false&market_data=false&community_data=false&developer_data=false",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CoinMetadata", $"CoinGecko API returned {response.StatusCode} for {symbol}");
                return null;
            }

            var coinData = await response.Content.ReadFromJsonAsync<CoinGeckoResponse>(cancellationToken: cancellationToken);

            // Get the small icon (64x64)
            var iconUrl = coinData?.Image?.Small ?? coinData?.Image?.Thumb;
            if (string.IsNullOrEmpty(iconUrl))
            {
                _logger.LogWarning("CoinMetadata", $"No icon URL found for {symbol}");
                return null;
            }

            // Download the icon
            var iconData = await _httpClient.GetByteArrayAsync(iconUrl, cancellationToken);

            // Save metadata to database
            await SaveMetadataToDatabaseAsync(symbol, coinData?.Name, iconUrl, geckoId, coinData?.MarketCapRank);

            _logger.LogInfo("CoinMetadata", $"Fetched icon for {symbol} ({iconData.Length} bytes)");
            return iconData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("CoinMetadata", $"Failed to fetch icon for {symbol}: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> SearchCoinGeckoIdAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // Search for coin by symbol
            var response = await _httpClient.GetAsync(
                $"{CoinGeckoApiBase}/search?query={symbol}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var searchResult = await response.Content.ReadFromJsonAsync<CoinGeckoSearchResponse>(cancellationToken: cancellationToken);

            // Find exact symbol match
            var coin = searchResult?.Coins?.FirstOrDefault(c =>
                string.Equals(c.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            return coin?.Id;
        }
        catch
        {
            return null;
        }
    }

    private async Task<CoinMetadata?> GetFromDatabaseAsync(string symbol)
    {
        try
        {
            var sql = "SELECT * FROM CoinMetadata WHERE Symbol = @Symbol";
            var entity = await _database.QueryFirstOrDefaultAsync<CoinMetadataEntity>(sql, new { Symbol = symbol.ToUpperInvariant() });

            if (entity == null)
                return null;

            return new CoinMetadata
            {
                Symbol = entity.Symbol,
                Name = entity.Name,
                IconUrl = entity.IconUrl,
                IconData = entity.IconData,
                CoinGeckoId = entity.CoinGeckoId,
                MarketCapRank = entity.MarketCapRank,
                LastUpdated = DateTime.TryParse(entity.LastUpdated, out var dt) ? dt : DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveToDatabaseAsync(string symbol, byte[] iconData)
    {
        try
        {
            var sql = @"
                INSERT OR REPLACE INTO CoinMetadata (Symbol, IconData, LastUpdated)
                VALUES (@Symbol, @IconData, datetime('now'))
            ";
            await _database.ExecuteAsync(sql, new
            {
                Symbol = symbol.ToUpperInvariant(),
                IconData = iconData
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("CoinMetadata", $"Failed to save icon to database: {ex.Message}");
        }
    }

    private async Task SaveMetadataToDatabaseAsync(string symbol, string? name, string? iconUrl, string? geckoId, int? marketCapRank)
    {
        try
        {
            var sql = @"
                INSERT OR REPLACE INTO CoinMetadata (Symbol, Name, IconUrl, CoinGeckoId, MarketCapRank, LastUpdated)
                VALUES (@Symbol, @Name, @IconUrl, @CoinGeckoId, @MarketCapRank, datetime('now'))
            ";
            await _database.ExecuteAsync(sql, new
            {
                Symbol = symbol.ToUpperInvariant(),
                Name = name,
                IconUrl = iconUrl,
                CoinGeckoId = geckoId,
                MarketCapRank = marketCapRank
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("CoinMetadata", $"Failed to save metadata to database: {ex.Message}");
        }
    }

    private static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return "";

        // Extract base asset from pair (e.g., "BTC/USDT" -> "BTC")
        if (symbol.Contains('/'))
            symbol = symbol.Split('/')[0];
        else if (symbol.Contains('-'))
            symbol = symbol.Split('-')[0];
        else if (symbol.Contains('_'))
            symbol = symbol.Split('_').Last(); // For Bitkub: THB_BTC -> BTC

        return symbol.ToUpperInvariant();
    }
}

// CoinGecko API response models
internal class CoinGeckoResponse
{
    public string? Id { get; set; }
    public string? Symbol { get; set; }
    public string? Name { get; set; }
    public CoinGeckoImage? Image { get; set; }

    [JsonPropertyName("market_cap_rank")]
    public int? MarketCapRank { get; set; }
}

internal class CoinGeckoImage
{
    public string? Thumb { get; set; }
    public string? Small { get; set; }
    public string? Large { get; set; }
}

internal class CoinGeckoSearchResponse
{
    public List<CoinGeckoSearchCoin>? Coins { get; set; }
}

internal class CoinGeckoSearchCoin
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Symbol { get; set; }
    public string? Thumb { get; set; }
}

internal class CoinMetadataEntity
{
    public string Symbol { get; set; } = "";
    public string? Name { get; set; }
    public string? IconUrl { get; set; }
    public byte[]? IconData { get; set; }
    public string? CoinGeckoId { get; set; }
    public int? MarketCapRank { get; set; }
    public string? LastUpdated { get; set; }
}
