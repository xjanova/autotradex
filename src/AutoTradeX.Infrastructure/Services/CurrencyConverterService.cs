// AutoTrade-X v1.0.0 - Currency Converter Service Implementation

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AutoTradeX.Core.Interfaces;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// CurrencyConverterService - Real-time currency conversion using Bitkub's public API
/// Fetches THB/USDT rate from Bitkub and caches it for performance
/// </summary>
public class CurrencyConverterService : ICurrencyConverterService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService? _logger;
    private readonly SemaphoreSlim _rateLock = new(1, 1);

    private decimal _cachedThbUsdtRate = 35.0m; // Default fallback rate
    private DateTime _lastRateUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(1); // Refresh rate every minute

    public DateTime LastRateUpdate => _lastRateUpdate;
    public string RateSource { get; private set; } = "Default";

    public CurrencyConverterService(ILoggingService? logger = null)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.bitkub.com"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// Get the current THB/USDT exchange rate from Bitkub
    /// Uses caching to avoid excessive API calls
    /// </summary>
    public async Task<decimal> GetThbUsdtRateAsync(CancellationToken cancellationToken = default)
    {
        // Check if cache is still valid
        if (_lastRateUpdate > DateTime.MinValue &&
            DateTime.UtcNow - _lastRateUpdate < _cacheExpiration)
        {
            return _cachedThbUsdtRate;
        }

        await _rateLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_lastRateUpdate > DateTime.MinValue &&
                DateTime.UtcNow - _lastRateUpdate < _cacheExpiration)
            {
                return _cachedThbUsdtRate;
            }

            await FetchRateFromBitkubAsync(cancellationToken);
            return _cachedThbUsdtRate;
        }
        finally
        {
            _rateLock.Release();
        }
    }

    /// <summary>
    /// Fetch rate from Bitkub's public ticker API
    /// </summary>
    private async Task FetchRateFromBitkubAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Bitkub's public API for USDT/THB ticker
            var response = await _httpClient.GetFromJsonAsync<Dictionary<string, BitkubTickerInfo>>(
                "/api/market/ticker",
                cancellationToken);

            if (response != null && response.TryGetValue("THB_USDT", out var usdtTicker))
            {
                // The last price is THB per 1 USDT
                _cachedThbUsdtRate = usdtTicker.Last;
                _lastRateUpdate = DateTime.UtcNow;
                RateSource = "Bitkub";

                _logger?.LogInfo("CurrencyConverter", $"Updated THB/USDT rate: {_cachedThbUsdtRate:N2} THB (from Bitkub)");
            }
            else
            {
                // Fallback: Try to get rate from a free exchange rate API
                await FetchRateFromFallbackAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("CurrencyConverter", $"Failed to fetch rate from Bitkub: {ex.Message}, trying fallback...");

            // Try fallback API
            try
            {
                await FetchRateFromFallbackAsync(cancellationToken);
            }
            catch (Exception fallbackEx)
            {
                _logger?.LogError("CurrencyConverter", $"Fallback also failed: {fallbackEx.Message}, using cached rate");
                // Keep using the last known rate
            }
        }
    }

    /// <summary>
    /// Fallback: Try multiple free public APIs that don't require registration
    /// Priority: 1. FloatRates (XML/JSON) 2. Open Exchange Rates (free) 3. Frankfurter (ECB data)
    /// </summary>
    private async Task FetchRateFromFallbackAsync(CancellationToken cancellationToken)
    {
        using var fallbackClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        fallbackClient.DefaultRequestHeaders.Add("User-Agent", "AutoTradeX/1.0");

        // Try multiple fallback sources
        var fallbackSources = new List<Func<Task<bool>>>
        {
            // 1. FloatRates - Reliable free API, no registration needed
            async () => await TryFloatRatesAsync(fallbackClient, cancellationToken),

            // 2. Open Exchange Rates (free tier with app_id=free)
            async () => await TryOpenExchangeRatesAsync(fallbackClient, cancellationToken),

            // 3. Frankfurter API - ECB data, very reliable
            async () => await TryFrankfurterAsync(fallbackClient, cancellationToken),

            // 4. ExchangeRate-API v4 (open/free)
            async () => await TryExchangeRateApiAsync(fallbackClient, cancellationToken),
        };

        foreach (var trySource in fallbackSources)
        {
            try
            {
                if (await trySource())
                {
                    return; // Success, no need to try more
                }
            }
            catch
            {
                // Try next source
            }
        }

        _logger?.LogWarning("CurrencyConverter", "All fallback sources failed, using cached/default rate");
    }

    /// <summary>
    /// FloatRates - Free reliable API (floatrates.com)
    /// </summary>
    private async Task<bool> TryFloatRatesAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetFromJsonAsync<FloatRatesResponse>(
            "https://www.floatrates.com/daily/usd.json", ct);

        if (response?.Thb != null)
        {
            _cachedThbUsdtRate = response.Thb.Rate;
            _lastRateUpdate = DateTime.UtcNow;
            RateSource = "FloatRates";
            _logger?.LogInfo("CurrencyConverter", $"Updated THB/USDT rate: {_cachedThbUsdtRate:N2} THB (from FloatRates)");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Open Exchange Rates - Has a free tier
    /// </summary>
    private async Task<bool> TryOpenExchangeRatesAsync(HttpClient client, CancellationToken ct)
    {
        // Note: This uses a sample/demo endpoint that may have limitations
        var json = await client.GetStringAsync(
            "https://open.er-api.com/v6/latest/USD", ct);

        var response = System.Text.Json.JsonSerializer.Deserialize<OpenExchangeRatesResponse>(json);
        if (response?.Rates != null && response.Rates.TryGetValue("THB", out var thbRate))
        {
            _cachedThbUsdtRate = thbRate;
            _lastRateUpdate = DateTime.UtcNow;
            RateSource = "Open Exchange Rates";
            _logger?.LogInfo("CurrencyConverter", $"Updated THB/USDT rate: {_cachedThbUsdtRate:N2} THB (from Open ER)");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Frankfurter API - ECB reference rates (very reliable, EU institution)
    /// </summary>
    private async Task<bool> TryFrankfurterAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetFromJsonAsync<FrankfurterResponse>(
            "https://api.frankfurter.app/latest?from=USD&to=THB", ct);

        if (response?.Rates != null && response.Rates.TryGetValue("THB", out var thbRate))
        {
            _cachedThbUsdtRate = thbRate;
            _lastRateUpdate = DateTime.UtcNow;
            RateSource = "Frankfurter (ECB)";
            _logger?.LogInfo("CurrencyConverter", $"Updated THB/USDT rate: {_cachedThbUsdtRate:N2} THB (from Frankfurter)");
            return true;
        }
        return false;
    }

    /// <summary>
    /// ExchangeRate-API v4 (free open API)
    /// </summary>
    private async Task<bool> TryExchangeRateApiAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetFromJsonAsync<ExchangeRateApiResponse>(
            "https://api.exchangerate-api.com/v4/latest/USD", ct);

        if (response?.Rates != null && response.Rates.TryGetValue("THB", out var thbRate))
        {
            _cachedThbUsdtRate = thbRate;
            _lastRateUpdate = DateTime.UtcNow;
            RateSource = "ExchangeRate-API";
            _logger?.LogInfo("CurrencyConverter", $"Updated THB/USDT rate: {_cachedThbUsdtRate:N2} THB (from ExchangeRate-API)");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Convert USDT to THB
    /// </summary>
    public async Task<decimal> ConvertUsdtToThbAsync(decimal usdtAmount, CancellationToken cancellationToken = default)
    {
        var rate = await GetThbUsdtRateAsync(cancellationToken);
        return usdtAmount * rate;
    }

    /// <summary>
    /// Convert THB to USDT
    /// </summary>
    public async Task<decimal> ConvertThbToUsdtAsync(decimal thbAmount, CancellationToken cancellationToken = default)
    {
        var rate = await GetThbUsdtRateAsync(cancellationToken);
        if (rate == 0) return 0;
        return thbAmount / rate;
    }

    /// <summary>
    /// Get the cached rate without making a network call
    /// </summary>
    public decimal GetCachedThbUsdtRate()
    {
        return _cachedThbUsdtRate;
    }

    /// <summary>
    /// Force refresh the rate from API
    /// </summary>
    public async Task RefreshRateAsync(CancellationToken cancellationToken = default)
    {
        await _rateLock.WaitAsync(cancellationToken);
        try
        {
            _lastRateUpdate = DateTime.MinValue; // Force refresh
            await FetchRateFromBitkubAsync(cancellationToken);
        }
        finally
        {
            _rateLock.Release();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _rateLock.Dispose();
    }
}

#region API Response Models

internal class BitkubTickerInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("last")]
    public decimal Last { get; set; }

    [JsonPropertyName("lowestAsk")]
    public decimal LowestAsk { get; set; }

    [JsonPropertyName("highestBid")]
    public decimal HighestBid { get; set; }

    [JsonPropertyName("percentChange")]
    public decimal PercentChange { get; set; }

    [JsonPropertyName("baseVolume")]
    public decimal BaseVolume { get; set; }

    [JsonPropertyName("quoteVolume")]
    public decimal QuoteVolume { get; set; }

    [JsonPropertyName("high24hr")]
    public decimal High24hr { get; set; }

    [JsonPropertyName("low24hr")]
    public decimal Low24hr { get; set; }
}

internal class ExchangeRateApiResponse
{
    [JsonPropertyName("base")]
    public string? Base { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("rates")]
    public Dictionary<string, decimal>? Rates { get; set; }
}

/// <summary>
/// FloatRates API response model
/// </summary>
internal class FloatRatesResponse
{
    [JsonPropertyName("thb")]
    public FloatRatesCurrency? Thb { get; set; }
}

internal class FloatRatesCurrency
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [JsonPropertyName("inverseRate")]
    public decimal InverseRate { get; set; }
}

/// <summary>
/// Open Exchange Rates API response model
/// </summary>
internal class OpenExchangeRatesResponse
{
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("base_code")]
    public string? BaseCode { get; set; }

    [JsonPropertyName("rates")]
    public Dictionary<string, decimal>? Rates { get; set; }
}

/// <summary>
/// Frankfurter API response model (ECB data)
/// </summary>
internal class FrankfurterResponse
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("base")]
    public string? Base { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("rates")]
    public Dictionary<string, decimal>? Rates { get; set; }
}

#endregion
