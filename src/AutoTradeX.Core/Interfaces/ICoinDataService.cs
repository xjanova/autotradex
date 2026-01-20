using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// ICoinDataService - Interface for fetching coin data from external APIs
/// Supports CoinGecko, CoinMarketCap, etc.
/// </summary>
public interface ICoinDataService
{
    /// <summary>
    /// Fetch prices for multiple coins
    /// </summary>
    Task<Dictionary<string, CoinPriceData>> GetPricesAsync(IEnumerable<string> coinIds, string currency = "usd");

    /// <summary>
    /// Fetch price for a single coin
    /// </summary>
    Task<decimal> GetPriceAsync(string coinId, string currency = "usd");

    /// <summary>
    /// Get coin info including icon URL
    /// </summary>
    Task<CoinInfoData?> GetCoinInfoAsync(string coinId);

    /// <summary>
    /// Search coins by name or symbol
    /// </summary>
    Task<List<CoinSearchResultData>> SearchCoinsAsync(string query);

    /// <summary>
    /// Get top coins by market cap
    /// </summary>
    Task<List<CoinMarketDataItem>> GetTopCoinsAsync(int count = 100, string currency = "usd");

    /// <summary>
    /// Map symbol to provider-specific coin ID
    /// </summary>
    string GetCoinIdFromSymbol(string symbol);

    /// <summary>
    /// Get cached price for a symbol (synchronous, uses internal cache)
    /// </summary>
    decimal GetPrice(string symbol);

    /// <summary>
    /// Update internal price cache
    /// </summary>
    Task RefreshPriceCacheAsync(IEnumerable<string> symbols);
}

// DTO classes for coin data
public class CoinPriceData
{
    public string CoinId { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Change24h { get; set; }
    public decimal MarketCap { get; set; }
    public string Currency { get; set; } = "USD";
}

public class CoinInfoData
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string ImageUrlLarge { get; set; } = "";
    public decimal CurrentPrice { get; set; }
    public int MarketCapRank { get; set; }
}

public class CoinSearchResultData
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public int MarketCapRank { get; set; }
}

public class CoinMarketDataItem
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public decimal CurrentPrice { get; set; }
    public decimal MarketCap { get; set; }
    public int MarketCapRank { get; set; }
    public decimal PriceChange24h { get; set; }
    public decimal Volume24h { get; set; }
}
