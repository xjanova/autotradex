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

    /// <summary>
    /// ดึงข้อมูล API Permissions - ต้อง override ใน subclass
    /// Default: สมมติว่ามีสิทธิ์อ่านถ้าเชื่อมต่อได้
    /// </summary>
    public virtual async Task<ApiPermissionInfo> GetApiPermissionsAsync(CancellationToken cancellationToken = default)
    {
        // Default implementation - ลองดึง balance ถ้าได้แสดงว่ามีสิทธิ์อ่าน
        var permissions = new ApiPermissionInfo();

        try
        {
            if (HasCredentials())
            {
                var balance = await GetBalanceAsync(cancellationToken);
                permissions.CanRead = balance != null;
            }
        }
        catch
        {
            permissions.CanRead = false;
        }

        // ไม่สามารถตรวจสอบ Trade/Withdraw/Deposit ได้โดย default
        // ต้อง override ใน subclass แต่ละ exchange
        permissions.AdditionalInfo = "ไม่สามารถตรวจสอบสิทธิ์ได้โดยละเอียด - กรุณาตรวจสอบที่เว็บ exchange";

        return permissions;
    }

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

    /// <summary>
    /// Default implementation - ต้อง override ใน subclass แต่ละ Exchange
    /// เพราะแต่ละ exchange มี API endpoint ต่างกัน
    /// </summary>
    public virtual Task<Dictionary<string, Ticker>> GetAllTickersAsync(
        string? quoteAsset = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(ExchangeName, "GetAllTickersAsync not implemented - returning empty");
        return Task.FromResult(new Dictionary<string, Ticker>());
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

    /// <summary>
    /// Default implementation for GetKlinesAsync (Candlestick data)
    /// Override ใน subclass ถ้ามี API endpoint สำหรับ klines
    /// </summary>
    public virtual Task<List<PriceCandle>> GetKlinesAsync(
        string symbol, string interval = "1m", int limit = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(ExchangeName, $"GetKlinesAsync not implemented for {ExchangeName} - returning empty list");
        return Task.FromResult(new List<PriceCandle>());
    }

    /// <summary>
    /// Default implementation for GetDepositAddressAsync
    /// Override ใน subclass ถ้ามี API endpoint สำหรับดึง deposit address
    /// </summary>
    public virtual Task<DepositAddressInfo> GetDepositAddressAsync(
        string asset, string? network = null, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(ExchangeName, $"GetDepositAddressAsync not fully implemented for {ExchangeName} - returning placeholder");

        // Default: return info indicating user should check exchange website
        return Task.FromResult(new DepositAddressInfo
        {
            Asset = asset,
            Network = network ?? asset,
            Address = string.Empty,
            RequiredConfirmations = GetDefaultConfirmations(asset)
        });
    }

    /// <summary>
    /// Default implementation for GetWithdrawalFeeAsync
    /// Override ใน subclass ถ้ามี API endpoint สำหรับค่าถอน
    /// </summary>
    public virtual Task<WithdrawalFeeInfo> GetWithdrawalFeeAsync(
        string asset, string? network = null, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(ExchangeName, $"GetWithdrawalFeeAsync not fully implemented for {ExchangeName} - returning estimate");

        // Return common fee estimates
        var fee = GetEstimatedWithdrawalFee(asset, network);
        return Task.FromResult(fee);
    }

    /// <summary>
    /// Get estimated withdrawal fee for common assets
    /// ค่าธรรมเนียมถอนโดยประมาณสำหรับเหรียญทั่วไป
    /// </summary>
    protected static WithdrawalFeeInfo GetEstimatedWithdrawalFee(string asset, string? network)
    {
        var effectiveNetwork = network ?? asset;
        return (asset.ToUpperInvariant(), effectiveNetwork.ToUpperInvariant()) switch
        {
            ("BTC", "BTC") => new() { Asset = "BTC", Network = "BTC", Fee = 0.0005m, MinWithdrawalAmount = 0.001m },
            ("ETH", "ERC20") or ("ETH", "ETH") => new() { Asset = "ETH", Network = "ERC20", Fee = 0.005m, MinWithdrawalAmount = 0.01m },
            ("USDT", "TRC20") or ("USDT", "TRON") => new() { Asset = "USDT", Network = "TRC20", Fee = 1m, MinWithdrawalAmount = 10m },
            ("USDT", "ERC20") or ("USDT", "ETH") => new() { Asset = "USDT", Network = "ERC20", Fee = 10m, MinWithdrawalAmount = 20m },
            ("USDT", "BSC") or ("USDT", "BEP20") => new() { Asset = "USDT", Network = "BSC", Fee = 0.8m, MinWithdrawalAmount = 10m },
            ("USDT", "SOL") or ("USDT", "SOLANA") => new() { Asset = "USDT", Network = "SOL", Fee = 1m, MinWithdrawalAmount = 10m },
            ("XRP", _) => new() { Asset = "XRP", Network = "XRP", Fee = 0.25m, MinWithdrawalAmount = 20m },
            ("SOL", _) => new() { Asset = "SOL", Network = "SOL", Fee = 0.01m, MinWithdrawalAmount = 0.1m },
            ("BNB", "BSC") or ("BNB", "BEP20") => new() { Asset = "BNB", Network = "BSC", Fee = 0.005m, MinWithdrawalAmount = 0.01m },
            ("MATIC", _) or ("POL", _) => new() { Asset = asset, Network = "POLYGON", Fee = 0.1m, MinWithdrawalAmount = 1m },
            ("DOGE", _) => new() { Asset = "DOGE", Network = "DOGE", Fee = 5m, MinWithdrawalAmount = 10m },
            ("ADA", _) => new() { Asset = "ADA", Network = "ADA", Fee = 1m, MinWithdrawalAmount = 5m },
            _ => new() { Asset = asset, Network = effectiveNetwork, Fee = 0m, MinWithdrawalAmount = 0m }
        };
    }

    /// <summary>
    /// Get default required confirmations for common networks
    /// จำนวน confirmations เริ่มต้นสำหรับเครือข่ายทั่วไป
    /// </summary>
    protected static int GetDefaultConfirmations(string assetOrNetwork)
    {
        return assetOrNetwork.ToUpperInvariant() switch
        {
            "BTC" => 2,
            "ETH" or "ERC20" => 12,
            "TRC20" or "TRON" => 20,
            "BSC" or "BEP20" => 15,
            "SOL" or "SOLANA" => 1,
            "XRP" => 1,
            "DOGE" => 40,
            "ADA" => 15,
            "POLYGON" or "MATIC" => 30,
            _ => 6
        };
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
    /// Uses CancellationToken.None for the delayed release to prevent semaphore leaks
    /// when the caller's token is cancelled during the delay.
    /// </summary>
    private async Task WaitForRateLimitAsync(CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);

        // ปล่อย semaphore หลังจากผ่านไป 1 วินาที
        // Use CancellationToken.None so the release always happens even if caller cancels
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, CancellationToken.None);
            }
            finally
            {
                _rateLimiter.Release();
            }
        }, CancellationToken.None);
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
