/*
 * ============================================================================
 * AutoTrade-X - Cross-Exchange Arbitrage Trading Bot
 * ============================================================================
 * ⚠️ Educational/Experimental Only - No profit guarantee
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace AutoTradeX.Infrastructure.ExchangeClients;

/// <summary>
/// BaseExchangeClient - Base class สำหรับ Exchange Client ทั้งหมด
/// มีฟังก์ชันพื้นฐานที่ใช้ร่วมกัน เช่น HTTP requests, error handling, rate limiting
/// </summary>
public abstract class BaseExchangeClient : IExchangeClient
{
    protected readonly HttpClient _httpClient;
    protected readonly ILoggingService _logger;
    protected readonly ExchangeConfig _config;
    protected readonly JsonSerializerOptions _jsonOptions;

    // Rate limiting
    private readonly SemaphoreSlim _rateLimiter;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly object _rateLimitLock = new();

    public abstract string ExchangeName { get; }
    public bool IsConnected { get; protected set; }

    protected BaseExchangeClient(ExchangeConfig config, ILoggingService logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.ApiBaseUrl),
            Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs)
        };

        _rateLimiter = new SemaphoreSlim(config.RateLimitPerSecond, config.RateLimitPerSecond);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    #region Abstract Methods - ต้อง implement ใน subclass

    public abstract Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default);
    public abstract Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default);
    public abstract Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default);
    public abstract Task<Order> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken = default);
    public abstract Task<Order> CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default);
    public abstract Task<Order> GetOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default);

    #endregion

    #region Virtual Methods - override ได้ถ้าต้องการ

    public virtual async Task<Dictionary<string, Ticker>> GetTickersAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();
        var tasks = symbols.Select(async symbol =>
        {
            var ticker = await GetTickerAsync(symbol, cancellationToken);
            return (symbol, ticker);
        });

        var tickers = await Task.WhenAll(tasks);
        foreach (var (symbol, ticker) in tickers)
        {
            result[symbol] = ticker;
        }

        return result;
    }

    public virtual async Task<AssetBalance> GetAssetBalanceAsync(
        string asset,
        CancellationToken cancellationToken = default)
    {
        var balance = await GetBalanceAsync(cancellationToken);
        return balance.Assets.TryGetValue(asset.ToUpperInvariant(), out var assetBalance)
            ? assetBalance
            : new AssetBalance { Asset = asset };
    }

    public virtual async Task<List<Order>> GetOpenOrdersAsync(
        string? symbol = null,
        CancellationToken cancellationToken = default)
    {
        // Default implementation - override ใน subclass ที่ต้องการ
        _logger.LogWarning(ExchangeName, "GetOpenOrdersAsync not implemented");
        return new List<Order>();
    }

    public virtual Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        _logger.LogInfo(ExchangeName, "Connected");
        return Task.CompletedTask;
    }

    public virtual Task DisconnectAsync()
    {
        IsConnected = false;
        _logger.LogInfo(ExchangeName, "Disconnected");
        return Task.CompletedTask;
    }

    public virtual async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // ลองดึง ticker ของ BTC/USDT เพื่อทดสอบ
            await GetTickerAsync("BTCUSDT", cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region HTTP Helpers

    /// <summary>
    /// ส่ง GET request พร้อม rate limiting และ retry
    /// </summary>
    protected async Task<T?> GetAsync<T>(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        await WaitForRateLimitAsync(cancellationToken);

        for (int retry = 0; retry <= _config.MaxRetries; retry++)
        {
            try
            {
                var response = await _httpClient.GetAsync(endpoint, cancellationToken);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
            }
            catch (HttpRequestException ex) when (retry < _config.MaxRetries)
            {
                _logger.LogWarning(ExchangeName, $"Request failed (retry {retry + 1}/{_config.MaxRetries}): {ex.Message}");
                await Task.Delay(1000 * (retry + 1), cancellationToken);
            }
        }

        throw new Exception($"Failed to GET {endpoint} after {_config.MaxRetries} retries");
    }

    /// <summary>
    /// ส่ง POST request
    /// </summary>
    protected async Task<T?> PostAsync<T>(
        string endpoint,
        object data,
        CancellationToken cancellationToken = default)
    {
        await WaitForRateLimitAsync(cancellationToken);

        for (int retry = 0; retry <= _config.MaxRetries; retry++)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(endpoint, data, _jsonOptions, cancellationToken);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
            }
            catch (HttpRequestException ex) when (retry < _config.MaxRetries)
            {
                _logger.LogWarning(ExchangeName, $"Request failed (retry {retry + 1}/{_config.MaxRetries}): {ex.Message}");
                await Task.Delay(1000 * (retry + 1), cancellationToken);
            }
        }

        throw new Exception($"Failed to POST {endpoint} after {_config.MaxRetries} retries");
    }

    /// <summary>
    /// Rate limiting - รอถ้าเกิน limit
    /// </summary>
    private async Task WaitForRateLimitAsync(CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);

        try
        {
            // ปล่อย semaphore หลังจากผ่านไป 1 วินาที
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000, ct);
                _rateLimiter.Release();
            }, ct);
        }
        catch (OperationCanceledException)
        {
            _rateLimiter.Release();
            throw;
        }
    }

    #endregion

    #region API Authentication Helpers

    /// <summary>
    /// ดึง API Key จาก Environment Variable
    /// </summary>
    protected string? GetApiKey()
    {
        return Environment.GetEnvironmentVariable(_config.ApiKeyEnvVar);
    }

    /// <summary>
    /// ดึง API Secret จาก Environment Variable
    /// </summary>
    protected string? GetApiSecret()
    {
        return Environment.GetEnvironmentVariable(_config.ApiSecretEnvVar);
    }

    /// <summary>
    /// ตรวจสอบว่ามี credentials หรือไม่
    /// </summary>
    protected bool HasCredentials()
    {
        return !string.IsNullOrEmpty(GetApiKey()) && !string.IsNullOrEmpty(GetApiSecret());
    }

    #endregion

    public virtual void Dispose()
    {
        _httpClient?.Dispose();
        _rateLimiter?.Dispose();
    }
}
