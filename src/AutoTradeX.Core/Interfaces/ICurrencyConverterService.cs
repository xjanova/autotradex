// AutoTrade-X v1.0.0 - Currency Converter Service Interface

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// ICurrencyConverterService - Service for converting between currencies
/// Uses real-time exchange rates from public APIs
/// </summary>
public interface ICurrencyConverterService
{
    /// <summary>
    /// Get the current THB/USDT exchange rate
    /// </summary>
    /// <returns>THB per 1 USDT</returns>
    Task<decimal> GetThbUsdtRateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Convert USDT to THB
    /// </summary>
    /// <param name="usdtAmount">Amount in USDT</param>
    /// <returns>Amount in THB</returns>
    Task<decimal> ConvertUsdtToThbAsync(decimal usdtAmount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convert THB to USDT
    /// </summary>
    /// <param name="thbAmount">Amount in THB</param>
    /// <returns>Amount in USDT</returns>
    Task<decimal> ConvertThbToUsdtAsync(decimal thbAmount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cached THB/USDT rate (no network call, returns last known rate)
    /// </summary>
    decimal GetCachedThbUsdtRate();

    /// <summary>
    /// Force refresh the exchange rate from API
    /// </summary>
    Task RefreshRateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Last update time of the rate
    /// </summary>
    DateTime LastRateUpdate { get; }

    /// <summary>
    /// Current rate source (e.g., "Bitkub", "ExchangeRate-API")
    /// </summary>
    string RateSource { get; }
}
