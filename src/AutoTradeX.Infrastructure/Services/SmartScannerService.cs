/*
 * ============================================================================
 * AutoTrade-X - Smart Scanner Service (PRODUCTION VERSION)
 * ============================================================================
 * Intelligent coin scanning with multiple strategies using REAL exchange data
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Collections.Concurrent;

namespace AutoTradeX.Infrastructure.Services;

public enum ScanStrategy
{
    ArbitrageBest,      // หา spread ที่ดีที่สุดระหว่าง exchange
    PriceDrop,          // หาเหรียญที่ราคาลดลงมาก (buy low)
    HighVolatility,     // หาเหรียญที่ผันผวนสูง (trade swings)
    VolumeSurge,        // หาเหรียญที่ volume เพิ่มขึ้นผิดปกติ
    MomentumUp,         // หาเหรียญที่กำลังขึ้น
    MomentumDown,       // หาเหรียญที่กำลังลง (short opportunity)
    NewListings,        // เหรียญใหม่บน exchange
    TopGainers,         // เหรียญที่ขึ้นมากที่สุดวันนี้
    TopLosers           // เหรียญที่ลงมากที่สุดวันนี้
}

public class ScanResult
{
    public string Symbol { get; set; } = "";
    public string BaseAsset { get; set; } = "";
    public string QuoteAsset { get; set; } = "USDT";
    public decimal CurrentPrice { get; set; }
    public decimal PriceChange24h { get; set; }
    public decimal Volume24h { get; set; }
    public decimal Volatility { get; set; }
    public decimal MarketCap { get; set; }
    public int MarketCapRank { get; set; }
    public string? ImageUrl { get; set; }

    // Arbitrage specific
    public decimal BestBuyPrice { get; set; }
    public decimal BestSellPrice { get; set; }
    public string BestBuyExchange { get; set; } = "";
    public string BestSellExchange { get; set; } = "";
    public decimal SpreadPercent { get; set; }
    public decimal EstimatedProfit { get; set; }

    // Scoring
    public decimal Score { get; set; }           // คะแนนรวม 0-100
    public string ScoreReason { get; set; } = "";
    public ScanStrategy MatchedStrategy { get; set; }

    // Available exchanges
    public List<ExchangePrice> ExchangePrices { get; set; } = new();

    // Timestamp
    public DateTime ScanTime { get; set; } = DateTime.UtcNow;
    public bool IsRecommended { get; set; }
}

public class ExchangePrice
{
    public string Exchange { get; set; } = "";
    public decimal BidPrice { get; set; }
    public decimal AskPrice { get; set; }
    public decimal Volume24h { get; set; }
    public decimal Spread { get; set; }
    public DateTime UpdateTime { get; set; }
    public bool IsLive { get; set; } = true;
}

public interface ISmartScannerService
{
    Task<List<ScanResult>> ScanAsync(ScanStrategy strategy, ScanOptions? options = null);
    Task<List<ScanResult>> ScanAllStrategiesAsync(ScanOptions? options = null);
    Task<ScanResult?> AnalyzeCoinAsync(string symbol);
    List<string> GetSupportedExchanges();
    List<string> GetSupportedSymbols();
    ScanResult? GetBestOpportunity();
    event EventHandler<ScanResult>? OpportunityFound;
}

public class ScanOptions
{
    public decimal MinSpreadPercent { get; set; } = 0.1m;
    public decimal MinVolume24h { get; set; } = 100000m;
    public decimal MinPriceChange { get; set; } = -5m;
    public decimal MaxPriceChange { get; set; } = 50m;
    public int MaxResults { get; set; } = 50;
    public List<string>? FilterSymbols { get; set; }
    public List<string>? FilterExchanges { get; set; }
    public bool IncludeLowVolume { get; set; } = false;
    public bool OnlyTradeable { get; set; } = true;
}

public class SmartScannerService : ISmartScannerService
{
    private readonly ICoinDataService _coinDataService;
    private readonly IExchangeClientFactory _exchangeFactory;
    private readonly ILoggingService _logger;
    private readonly List<ScanResult> _lastResults = new();
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastExchangeError = new();

    public event EventHandler<ScanResult>? OpportunityFound;

    // Supported exchanges for real trading
    private static readonly string[] SupportedExchanges = new[]
    {
        "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub"
    };

    // Popular trading pairs
    private static readonly string[] PopularSymbols = new[]
    {
        "BTC", "ETH", "SOL", "XRP", "DOGE", "ADA", "AVAX", "DOT", "LINK", "MATIC",
        "SHIB", "LTC", "TRX", "UNI", "ATOM", "XLM", "NEAR", "APT", "ARB", "OP",
        "AAVE", "TON", "INJ", "FIL", "ICP", "VET", "HBAR", "SUI", "SEI", "PEPE",
        "WIF", "BONK", "FLOKI", "FTM", "SAND", "MANA", "AXS", "GRT", "RUNE", "LDO",
        "MKR", "SNX", "CRV", "1INCH", "ENS", "BLUR", "STX", "IMX", "RENDER", "FET"
    };

    public SmartScannerService(
        ICoinDataService coinDataService,
        IExchangeClientFactory exchangeFactory,
        ILoggingService logger)
    {
        _coinDataService = coinDataService;
        _exchangeFactory = exchangeFactory;
        _logger = logger;
    }

    public List<string> GetSupportedExchanges() => SupportedExchanges.ToList();
    public List<string> GetSupportedSymbols() => PopularSymbols.ToList();

    public async Task<List<ScanResult>> ScanAsync(ScanStrategy strategy, ScanOptions? options = null)
    {
        options ??= new ScanOptions();
        var results = new List<ScanResult>();

        try
        {
            _logger.LogInfo("SmartScanner", $"Starting LIVE scan with strategy: {strategy}");

            // Get market data from CoinGecko for basic info
            var topCoins = await _coinDataService.GetTopCoinsAsync(100);

            // Determine which exchanges to scan
            var exchangesToScan = options.FilterExchanges?.Any() == true
                ? options.FilterExchanges.Intersect(SupportedExchanges, StringComparer.OrdinalIgnoreCase).ToList()
                : SupportedExchanges.ToList();

            // Filter and score based on strategy
            foreach (var coin in topCoins)
            {
                if (options.FilterSymbols?.Any() == true &&
                    !options.FilterSymbols.Contains(coin.Symbol, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!options.IncludeLowVolume && coin.Volume24h < options.MinVolume24h)
                {
                    continue;
                }

                var result = new ScanResult
                {
                    Symbol = $"{coin.Symbol}/USDT",
                    BaseAsset = coin.Symbol,
                    CurrentPrice = coin.CurrentPrice,
                    PriceChange24h = coin.PriceChange24h,
                    Volume24h = coin.Volume24h,
                    MarketCap = coin.MarketCap,
                    MarketCapRank = coin.MarketCapRank,
                    ImageUrl = coin.ImageUrl,
                    MatchedStrategy = strategy
                };

                // Fetch REAL prices from exchanges
                await FetchRealExchangePricesAsync(result, exchangesToScan);

                // Calculate score based on strategy
                var (score, reason) = CalculateScore(result, strategy, options);
                result.Score = score;
                result.ScoreReason = reason;

                if (score > 0)
                {
                    results.Add(result);
                }
            }

            // Sort by score
            results = results
                .OrderByDescending(r => r.Score)
                .Take(options.MaxResults)
                .ToList();

            // Mark top recommendations
            foreach (var result in results.Take(3))
            {
                result.IsRecommended = true;
            }

            lock (_lock)
            {
                _lastResults.Clear();
                _lastResults.AddRange(results);
            }

            // Fire event for best opportunity
            if (results.Any() && results[0].Score >= 70)
            {
                OpportunityFound?.Invoke(this, results[0]);
            }

            _logger.LogInfo("SmartScanner", $"LIVE scan complete: {results.Count} results found");
        }
        catch (Exception ex)
        {
            _logger.LogError("SmartScanner", $"Scan error: {ex.Message}");
        }

        return results;
    }

    public async Task<List<ScanResult>> ScanAllStrategiesAsync(ScanOptions? options = null)
    {
        var allResults = new List<ScanResult>();

        foreach (ScanStrategy strategy in Enum.GetValues<ScanStrategy>())
        {
            var results = await ScanAsync(strategy, options);
            allResults.AddRange(results);
        }

        // Remove duplicates, keep highest score
        var grouped = allResults
            .GroupBy(r => r.Symbol)
            .Select(g => g.OrderByDescending(r => r.Score).First())
            .OrderByDescending(r => r.Score)
            .Take(options?.MaxResults ?? 50)
            .ToList();

        return grouped;
    }

    public async Task<ScanResult?> AnalyzeCoinAsync(string symbol)
    {
        try
        {
            var coinId = _coinDataService.GetCoinIdFromSymbol(symbol);
            var coinInfo = await _coinDataService.GetCoinInfoAsync(coinId);

            if (coinInfo == null) return null;

            var result = new ScanResult
            {
                Symbol = $"{symbol}/USDT",
                BaseAsset = symbol.ToUpperInvariant(),
                CurrentPrice = coinInfo.CurrentPrice,
                MarketCapRank = coinInfo.MarketCapRank,
                ImageUrl = coinInfo.ImageUrl
            };

            // Fetch REAL prices
            await FetchRealExchangePricesAsync(result, SupportedExchanges.ToList());

            // Calculate score for arbitrage
            var (score, reason) = CalculateScore(result, ScanStrategy.ArbitrageBest, new ScanOptions());
            result.Score = score;
            result.ScoreReason = reason;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError("SmartScanner", $"Error analyzing {symbol}: {ex.Message}");
            return null;
        }
    }

    public ScanResult? GetBestOpportunity()
    {
        lock (_lock)
        {
            return _lastResults.FirstOrDefault();
        }
    }

    /// <summary>
    /// Fetch REAL prices from configured exchanges
    /// </summary>
    private async Task FetchRealExchangePricesAsync(ScanResult result, List<string> exchanges)
    {
        var symbol = result.BaseAsset + "USDT";
        var tasks = new List<Task<ExchangePrice?>>();

        foreach (var exchangeName in exchanges)
        {
            tasks.Add(FetchPriceFromExchangeAsync(exchangeName, symbol));
        }

        var prices = await Task.WhenAll(tasks);

        foreach (var price in prices.Where(p => p != null))
        {
            result.ExchangePrices.Add(price!);
        }

        // Calculate best arbitrage opportunity
        CalculateBestArbitrage(result);
    }

    private async Task<ExchangePrice?> FetchPriceFromExchangeAsync(string exchangeName, string symbol)
    {
        // Check if exchange recently errored (cool down)
        if (_lastExchangeError.TryGetValue(exchangeName, out var lastError) &&
            DateTime.UtcNow - lastError < TimeSpan.FromMinutes(1))
        {
            return null;
        }

        try
        {
            var client = _exchangeFactory.CreateClient(exchangeName);
            var ticker = await client.GetTickerAsync(symbol);

            if (ticker != null && ticker.BidPrice > 0 && ticker.AskPrice > 0)
            {
                return new ExchangePrice
                {
                    Exchange = exchangeName,
                    BidPrice = ticker.BidPrice,
                    AskPrice = ticker.AskPrice,
                    Volume24h = ticker.Volume24h,
                    Spread = (ticker.AskPrice - ticker.BidPrice) / ticker.AskPrice * 100,
                    UpdateTime = DateTime.UtcNow,
                    IsLive = true
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SmartScanner", $"Failed to fetch price from {exchangeName} for {symbol}: {ex.Message}");
            _lastExchangeError[exchangeName] = DateTime.UtcNow;
        }

        return null;
    }

    private void CalculateBestArbitrage(ScanResult result)
    {
        if (result.ExchangePrices.Count < 2) return;

        var bestBuy = result.ExchangePrices
            .Where(p => p.AskPrice > 0)
            .MinBy(p => p.AskPrice);

        var bestSell = result.ExchangePrices
            .Where(p => p.BidPrice > 0)
            .MaxBy(p => p.BidPrice);

        if (bestBuy != null && bestSell != null && bestBuy.Exchange != bestSell.Exchange)
        {
            result.BestBuyExchange = bestBuy.Exchange;
            result.BestBuyPrice = bestBuy.AskPrice;
            result.BestSellExchange = bestSell.Exchange;
            result.BestSellPrice = bestSell.BidPrice;
            result.SpreadPercent = (bestSell.BidPrice - bestBuy.AskPrice) / bestBuy.AskPrice * 100;

            // Estimate profit after fees (typical 0.1% per side = 0.2% total)
            var feePercent = 0.2m;
            result.EstimatedProfit = 1000m * (result.SpreadPercent - feePercent) / 100;
        }
    }

    private (decimal Score, string Reason) CalculateScore(ScanResult result, ScanStrategy strategy, ScanOptions options)
    {
        decimal score = 0;
        var reasons = new List<string>();

        switch (strategy)
        {
            case ScanStrategy.ArbitrageBest:
                // Score based on spread percentage
                if (result.SpreadPercent >= 0.3m)
                {
                    score = Math.Min(100, 50 + result.SpreadPercent * 100);
                    reasons.Add($"Spread {result.SpreadPercent:F2}%");
                }
                else if (result.SpreadPercent >= 0.1m)
                {
                    score = 30 + result.SpreadPercent * 100;
                    reasons.Add($"Moderate spread {result.SpreadPercent:F2}%");
                }
                break;

            case ScanStrategy.PriceDrop:
                // Score based on how much price dropped
                if (result.PriceChange24h <= -10)
                {
                    score = Math.Min(100, 50 + Math.Abs(result.PriceChange24h) * 2);
                    reasons.Add($"Price down {result.PriceChange24h:F1}%");
                }
                else if (result.PriceChange24h <= -5)
                {
                    score = 30 + Math.Abs(result.PriceChange24h) * 3;
                    reasons.Add($"Significant drop {result.PriceChange24h:F1}%");
                }
                break;

            case ScanStrategy.HighVolatility:
                // Score based on price change magnitude
                var volatility = Math.Abs(result.PriceChange24h);
                if (volatility >= 15)
                {
                    score = Math.Min(100, 50 + volatility * 2);
                    reasons.Add($"High volatility {volatility:F1}%");
                }
                else if (volatility >= 8)
                {
                    score = 30 + volatility * 3;
                    reasons.Add($"Moderate volatility {volatility:F1}%");
                }
                result.Volatility = volatility;
                break;

            case ScanStrategy.VolumeSurge:
                // High volume indicates interest
                if (result.Volume24h >= 500_000_000)
                {
                    score = 80;
                    reasons.Add($"Very high volume ${result.Volume24h / 1_000_000:F0}M");
                }
                else if (result.Volume24h >= 100_000_000)
                {
                    score = 60;
                    reasons.Add($"High volume ${result.Volume24h / 1_000_000:F0}M");
                }
                else if (result.Volume24h >= 10_000_000)
                {
                    score = 40;
                    reasons.Add($"Good volume ${result.Volume24h / 1_000_000:F0}M");
                }
                break;

            case ScanStrategy.MomentumUp:
                if (result.PriceChange24h >= 10)
                {
                    score = Math.Min(100, 50 + result.PriceChange24h * 2);
                    reasons.Add($"Strong momentum +{result.PriceChange24h:F1}%");
                }
                else if (result.PriceChange24h >= 5)
                {
                    score = 30 + result.PriceChange24h * 3;
                    reasons.Add($"Good momentum +{result.PriceChange24h:F1}%");
                }
                break;

            case ScanStrategy.MomentumDown:
                if (result.PriceChange24h <= -10)
                {
                    score = Math.Min(100, 50 + Math.Abs(result.PriceChange24h) * 2);
                    reasons.Add($"Strong down momentum {result.PriceChange24h:F1}%");
                }
                break;

            case ScanStrategy.TopGainers:
                if (result.PriceChange24h > 0)
                {
                    score = Math.Min(100, result.PriceChange24h * 3);
                    reasons.Add($"Gainer +{result.PriceChange24h:F1}%");
                }
                break;

            case ScanStrategy.TopLosers:
                if (result.PriceChange24h < 0)
                {
                    score = Math.Min(100, Math.Abs(result.PriceChange24h) * 3);
                    reasons.Add($"Loser {result.PriceChange24h:F1}%");
                }
                break;
        }

        // Bonus for high market cap (safer coins)
        if (result.MarketCapRank <= 10) score += 10;
        else if (result.MarketCapRank <= 50) score += 5;

        // Bonus for having multiple exchange prices (more reliable arbitrage)
        if (result.ExchangePrices.Count >= 4) score += 5;
        else if (result.ExchangePrices.Count >= 2) score += 2;

        // Ensure score is in range
        score = Math.Clamp(score, 0, 100);

        return (score, string.Join(", ", reasons));
    }
}
