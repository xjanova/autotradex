/*
 * ============================================================================
 * AutoTrade-X - Coin & Exchange Logo Service
 * ============================================================================
 * Smart logo fetching with multiple fallback sources
 * Caches logos locally for performance
 * ============================================================================
 */

using System.IO;
using System.Net.Http;
using AutoTradeX.Core.Interfaces;

namespace AutoTradeX.Infrastructure.Services;

public interface ICoinLogoService
{
    Task<string?> GetCoinLogoPathAsync(string symbol);
    Task<string?> GetExchangeLogoPathAsync(string exchangeName);
    Task<byte[]?> GetCoinLogoAsync(string symbol);
    Task<byte[]?> GetExchangeLogoAsync(string exchangeName);
    Task PreloadLogosAsync(IEnumerable<string> symbols);
}

public class CoinLogoService : ICoinLogoService
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService _logger;
    private readonly string _cacheDir;
    private readonly Dictionary<string, string> _logoCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Multiple logo sources for reliability
    private static readonly string[] CoinLogoSources = new[]
    {
        "https://raw.githubusercontent.com/spothq/cryptocurrency-icons/master/128/color/{0}.png",
        "https://assets.coingecko.com/coins/images/{1}/small/{0}.png",
        "https://cryptoicons.org/api/icon/{0}/128",
        "https://cdn.jsdelivr.net/gh/atomiclabs/cryptocurrency-icons@1a63530be6e374711a8554f31b17e4cb92c25fa5/128/color/{0}.png"
    };

    // Exchange logo sources
    private static readonly Dictionary<string, string> ExchangeLogos = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Binance", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/binance.png" },
        { "KuCoin", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/kucoin.png" },
        { "OKX", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/okx.png" },
        { "Bybit", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/bybit.png" },
        { "Gate.io", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/gate.png" },
        { "Bitkub", "https://www.bitkub.com/static/images/icons/bitkub-icon.png" },
        { "Coinbase", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/coinbase.png" },
        { "Kraken", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/kraken.png" },
        { "Huobi", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/huobi.png" },
        { "MEXC", "https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/mexc.png" }
    };

    // CoinGecko coin IDs for image lookup
    private static readonly Dictionary<string, (string Id, int ImageId)> CoinGeckoIds = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BTC", ("bitcoin", 1) },
        { "ETH", ("ethereum", 279) },
        { "USDT", ("tether", 325) },
        { "BNB", ("binancecoin", 825) },
        { "SOL", ("solana", 4128) },
        { "XRP", ("ripple", 44) },
        { "USDC", ("usd-coin", 6319) },
        { "ADA", ("cardano", 975) },
        { "AVAX", ("avalanche-2", 12559) },
        { "DOGE", ("dogecoin", 5) },
        { "DOT", ("polkadot", 12171) },
        { "LINK", ("chainlink", 877) },
        { "MATIC", ("matic-network", 4713) },
        { "SHIB", ("shiba-inu", 11939) },
        { "LTC", ("litecoin", 2) },
        { "TRX", ("tron", 1094) },
        { "UNI", ("uniswap", 12504) },
        { "ATOM", ("cosmos", 1481) },
        { "XLM", ("stellar", 100) },
        { "NEAR", ("near", 10365) },
        { "APT", ("aptos", 26455) },
        { "ARB", ("arbitrum", 16547) },
        { "OP", ("optimism", 25244) },
        { "AAVE", ("aave", 12645) },
        { "TON", ("the-open-network", 17980) },
        { "INJ", ("injective-protocol", 12220) },
        { "FIL", ("filecoin", 12817) },
        { "ICP", ("internet-computer", 14495) },
        { "VET", ("vechain", 3077) },
        { "HBAR", ("hedera-hashgraph", 4642) },
        { "SUI", ("sui", 26375) },
        { "SEI", ("sei-network", 28205) },
        { "PEPE", ("pepe", 24478) },
        { "WIF", ("dogwifcoin", 28752) },
        { "BONK", ("bonk", 28600) },
        { "FLOKI", ("floki", 10804) },
        { "FTM", ("fantom", 4001) },
        { "SAND", ("the-sandbox", 12129) },
        { "MANA", ("decentraland", 1966) },
        { "AXS", ("axie-infinity", 6783) },
        { "GRT", ("the-graph", 13397) },
        { "RUNE", ("thorchain", 4157) },
        { "LDO", ("lido-dao", 13573) },
        { "MKR", ("maker", 1518) },
        { "SNX", ("havven", 2586) },
        { "CRV", ("curve-dao-token", 12124) },
        { "1INCH", ("1inch", 8104) },
        { "ENS", ("ethereum-name-service", 19898) },
        { "BLUR", ("blur", 27316) }
    };

    public CoinLogoService(ILoggingService logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AutoTrade-X/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);

        // Create cache directory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDir = Path.Combine(appData, "AutoTradeX", "LogoCache");
        Directory.CreateDirectory(Path.Combine(_cacheDir, "coins"));
        Directory.CreateDirectory(Path.Combine(_cacheDir, "exchanges"));
    }

    public async Task<string?> GetCoinLogoPathAsync(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        // Check cache first
        var cachePath = Path.Combine(_cacheDir, "coins", $"{symbol.ToLowerInvariant()}.png");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        // Try to download
        var logoData = await GetCoinLogoAsync(symbol);
        if (logoData != null)
        {
            try
            {
                await File.WriteAllBytesAsync(cachePath, logoData);
                return cachePath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("LogoService", $"Failed to cache logo for {symbol}: {ex.Message}");
            }
        }

        return null;
    }

    public async Task<string?> GetExchangeLogoPathAsync(string exchangeName)
    {
        var normalizedName = exchangeName.Replace(".", "").ToLowerInvariant();

        // Check cache first
        var cachePath = Path.Combine(_cacheDir, "exchanges", $"{normalizedName}.png");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        // Try to download
        var logoData = await GetExchangeLogoAsync(exchangeName);
        if (logoData != null)
        {
            try
            {
                await File.WriteAllBytesAsync(cachePath, logoData);
                return cachePath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("LogoService", $"Failed to cache logo for {exchangeName}: {ex.Message}");
            }
        }

        return null;
    }

    public async Task<byte[]?> GetCoinLogoAsync(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        // Extract base symbol from pair (e.g., "BTC/USDT" -> "BTC")
        if (symbol.Contains('/'))
        {
            symbol = symbol.Split('/')[0];
        }

        await _lock.WaitAsync();
        try
        {
            // Check memory cache
            if (_logoCache.TryGetValue($"coin_{symbol}", out var cached) && File.Exists(cached))
            {
                return await File.ReadAllBytesAsync(cached);
            }

            // Try multiple sources
            byte[]? logoData = null;

            // Source 1: CoinGecko API
            if (CoinGeckoIds.TryGetValue(symbol, out var geckoInfo))
            {
                logoData = await TryDownloadAsync($"https://assets.coingecko.com/coins/images/{geckoInfo.ImageId}/small/{geckoInfo.Id}.png");
            }

            // Source 2: Cryptocurrency Icons GitHub
            if (logoData == null)
            {
                logoData = await TryDownloadAsync($"https://raw.githubusercontent.com/spothq/cryptocurrency-icons/master/128/color/{symbol.ToLowerInvariant()}.png");
            }

            // Source 3: CDN fallback
            if (logoData == null)
            {
                logoData = await TryDownloadAsync($"https://cdn.jsdelivr.net/gh/atomiclabs/cryptocurrency-icons@1a63530be6e374711a8554f31b17e4cb92c25fa5/128/color/{symbol.ToLowerInvariant()}.png");
            }

            // Source 4: CryptoIcons API
            if (logoData == null)
            {
                logoData = await TryDownloadAsync($"https://cryptoicons.org/api/icon/{symbol.ToLowerInvariant()}/128");
            }

            if (logoData != null)
            {
                _logger.LogInfo("LogoService", $"Downloaded logo for {symbol}");
            }

            return logoData;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<byte[]?> GetExchangeLogoAsync(string exchangeName)
    {
        await _lock.WaitAsync();
        try
        {
            // Check memory cache
            if (_logoCache.TryGetValue($"exchange_{exchangeName}", out var cached) && File.Exists(cached))
            {
                return await File.ReadAllBytesAsync(cached);
            }

            byte[]? logoData = null;

            // Try known exchange logos
            if (ExchangeLogos.TryGetValue(exchangeName, out var url))
            {
                logoData = await TryDownloadAsync(url);
            }

            // Fallback: Try common patterns
            if (logoData == null)
            {
                var name = exchangeName.ToLowerInvariant().Replace(".", "").Replace(" ", "");
                logoData = await TryDownloadAsync($"https://raw.githubusercontent.com/nicehash/Cryptocurrency-Logos/master/exchanges/{name}.png");
            }

            if (logoData != null)
            {
                _logger.LogInfo("LogoService", $"Downloaded logo for {exchangeName}");
            }

            return logoData;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task PreloadLogosAsync(IEnumerable<string> symbols)
    {
        _logger.LogInfo("LogoService", $"Preloading logos for {symbols.Count()} symbols...");

        var tasks = symbols.Select(async symbol =>
        {
            try
            {
                await GetCoinLogoPathAsync(symbol);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("LogoService", $"Failed to preload logo for {symbol}: {ex.Message}");
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInfo("LogoService", "Logo preload complete");
    }

    private async Task<byte[]?> TryDownloadAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsByteArrayAsync();
                // Verify it's actually an image (check PNG/JPEG header)
                if (content.Length > 8 &&
                    ((content[0] == 0x89 && content[1] == 0x50) || // PNG
                     (content[0] == 0xFF && content[1] == 0xD8)))   // JPEG
                {
                    return content;
                }
            }
        }
        catch
        {
            // Silently fail, try next source
        }
        return null;
    }
}
