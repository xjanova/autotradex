/*
 * ============================================================================
 * AutoTrade-X - Connection Status Service Implementation
 * ============================================================================
 * Monitors exchange connections and validates configuration before trading
 * ============================================================================
 */

using System.Diagnostics;
using System.Timers;
using AutoTradeX.Core.Interfaces;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// Service for monitoring connection status and validating trading prerequisites
/// </summary>
public class ConnectionStatusService : IConnectionStatusService, IDisposable
{
    private readonly IExchangeClientFactory _exchangeFactory;
    private readonly IConfigService _configService;
    private readonly IStrategyService _strategyService;
    private readonly ILoggingService _logger;

    private ConnectionStatusSnapshot _currentStatus = new();
    private System.Timers.Timer? _monitoringTimer;
    private readonly object _statusLock = new();

    // Supported exchanges that require API keys
    private static readonly string[] SupportedExchanges = { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };

    public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

    public ConnectionStatusSnapshot CurrentStatus
    {
        get
        {
            lock (_statusLock)
            {
                return _currentStatus;
            }
        }
    }

    public bool CanStartTrading
    {
        get
        {
            var status = CurrentStatus;

            // Must have at least one fully connected exchange
            if (status.ConnectedExchangeCount < 1)
                return false;

            // Must have an active strategy (unless in demo mode)
            if (status.IsLiveMode && !status.HasActiveStrategy)
                return false;

            // Must have trading pairs configured
            if (!status.HasTradingPairs)
                return false;

            return true;
        }
    }

    public ConnectionStatusService(
        IExchangeClientFactory exchangeFactory,
        IConfigService configService,
        IStrategyService strategyService,
        ILoggingService logger)
    {
        _exchangeFactory = exchangeFactory;
        _configService = configService;
        _strategyService = strategyService;
        _logger = logger;
    }

    public List<string> GetMissingPrerequisites()
    {
        var missing = new List<string>();
        var status = CurrentStatus;
        var config = _configService.GetConfig();

        // Check exchanges
        if (status.ConnectedExchangeCount == 0)
        {
            missing.Add("No exchange connected - please configure API keys in Settings");
        }
        else
        {
            // Check if configured exchanges are connected
            if (!string.IsNullOrEmpty(config.ExchangeA.Name))
            {
                var exchangeA = status.Exchanges.GetValueOrDefault(config.ExchangeA.Name);
                if (exchangeA == null || !exchangeA.IsConnected || !exchangeA.HasValidCredentials)
                {
                    missing.Add($"Exchange A ({config.ExchangeA.Name}) not connected or missing API key");
                }
            }
            else
            {
                missing.Add("Exchange A not configured");
            }

            if (!string.IsNullOrEmpty(config.ExchangeB.Name))
            {
                var exchangeB = status.Exchanges.GetValueOrDefault(config.ExchangeB.Name);
                if (exchangeB == null || !exchangeB.IsConnected || !exchangeB.HasValidCredentials)
                {
                    missing.Add($"Exchange B ({config.ExchangeB.Name}) not connected or missing API key");
                }
            }
            else
            {
                missing.Add("Exchange B not configured");
            }
        }

        // Check strategy (for live mode)
        if (config.General.LiveTrading && !status.HasActiveStrategy)
        {
            missing.Add("No trading strategy selected - please select a strategy");
        }

        // Check trading pairs
        if (!status.HasTradingPairs)
        {
            missing.Add("No trading pairs configured - please add pairs from Scanner or Projects");
        }

        return missing;
    }

    public async Task<ConnectionStatusSnapshot> CheckAllConnectionsAsync()
    {
        var snapshot = new ConnectionStatusSnapshot
        {
            Timestamp = DateTime.UtcNow,
            IsLiveMode = _configService.GetConfig().General.LiveTrading
        };

        // Check all exchanges in parallel
        var tasks = SupportedExchanges.Select(async exchange =>
        {
            var status = await CheckExchangeConnectionAsync(exchange);
            return (exchange, status);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (exchange, status) in results)
        {
            snapshot.Exchanges[exchange] = status;
        }

        // Check strategy
        var strategies = await _strategyService.GetAllStrategiesAsync();
        var activeStrategy = strategies.FirstOrDefault(s => s.IsActive);
        snapshot.HasActiveStrategy = activeStrategy != null;
        snapshot.ActiveStrategyName = activeStrategy?.Name;

        // Check trading pairs (from ArbEngine or config)
        var config = _configService.GetConfig();
        snapshot.HasTradingPairs = config.TradingPairs?.Count > 0;
        snapshot.TradingPairCount = config.TradingPairs?.Count ?? 0;

        // Update current status
        lock (_statusLock)
        {
            _currentStatus = snapshot;
        }

        // Raise event
        ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs(snapshot, null, "Connection check completed"));

        _logger.LogInfo("ConnectionStatus", $"Connection check: {snapshot.ConnectedExchangeCount}/{snapshot.Exchanges.Count} exchanges connected");

        return snapshot;
    }

    public async Task<ExchangeConnectionStatus> CheckExchangeConnectionAsync(string exchangeName)
    {
        var status = new ExchangeConnectionStatus
        {
            ExchangeName = exchangeName,
            LastChecked = DateTime.UtcNow
        };

        try
        {
            // Check if credentials are configured
            var config = _configService.GetConfig();
            var exchangeConfig = exchangeName.ToLower() switch
            {
                "binance" => config.ExchangeA.Name?.ToLower() == "binance" ? config.ExchangeA :
                             config.ExchangeB.Name?.ToLower() == "binance" ? config.ExchangeB : null,
                "kucoin" => config.ExchangeA.Name?.ToLower() == "kucoin" ? config.ExchangeA :
                            config.ExchangeB.Name?.ToLower() == "kucoin" ? config.ExchangeB : null,
                "okx" => config.ExchangeA.Name?.ToLower() == "okx" ? config.ExchangeA :
                         config.ExchangeB.Name?.ToLower() == "okx" ? config.ExchangeB : null,
                "bybit" => config.ExchangeA.Name?.ToLower() == "bybit" ? config.ExchangeA :
                           config.ExchangeB.Name?.ToLower() == "bybit" ? config.ExchangeB : null,
                "gate.io" => config.ExchangeA.Name?.ToLower() == "gate.io" ? config.ExchangeA :
                             config.ExchangeB.Name?.ToLower() == "gate.io" ? config.ExchangeB : null,
                "bitkub" => config.ExchangeA.Name?.ToLower() == "bitkub" ? config.ExchangeA :
                            config.ExchangeB.Name?.ToLower() == "bitkub" ? config.ExchangeB : null,
                _ => null
            };

            // Check if this exchange is even configured
            var isConfigured = (config.ExchangeA.Name?.Equals(exchangeName, StringComparison.OrdinalIgnoreCase) ?? false) ||
                              (config.ExchangeB.Name?.Equals(exchangeName, StringComparison.OrdinalIgnoreCase) ?? false);

            if (!isConfigured)
            {
                status.IsConnected = false;
                status.HasValidCredentials = false;
                status.ErrorMessage = "Exchange not configured";
                return status;
            }

            // Check credentials via environment variables
            var hasCredentials = _configService.HasValidCredentials(exchangeConfig!);
            status.HasValidCredentials = hasCredentials;

            if (!hasCredentials)
            {
                status.IsConnected = false;
                status.ErrorMessage = "API key not configured";
                return status;
            }

            // Test actual connection with authenticated endpoint
            var client = _exchangeFactory.CreateClient(exchangeName);

            // Measure latency
            var sw = Stopwatch.StartNew();

            // Test authenticated endpoint - try to get balance
            try
            {
                var balance = await client.GetBalanceAsync();
                sw.Stop();

                status.IsConnected = true;
                // AccountBalance has Assets dictionary - check if we got valid data
                status.CanReadBalance = balance != null && balance.Assets.Count > 0;
                status.CanTrade = true; // Assume can trade if can read balance
                status.Latency = (int)sw.ElapsedMilliseconds;

                _logger.LogInfo("ConnectionStatus", $"{exchangeName}: Connected ({status.Latency}ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                status.IsConnected = false;
                status.CanReadBalance = false;
                status.CanTrade = false;
                status.ErrorMessage = ex.Message;
                status.Latency = (int)sw.ElapsedMilliseconds;

                _logger.LogWarning("ConnectionStatus", $"{exchangeName}: Connection failed - {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            status.IsConnected = false;
            status.ErrorMessage = ex.Message;
            _logger.LogError("ConnectionStatus", $"{exchangeName}: Error checking connection - {ex.Message}");
        }

        return status;
    }

    public async Task<ApiValidationResult> ValidateApiCredentialsAsync(string exchangeName)
    {
        var result = new ApiValidationResult();

        try
        {
            var client = _exchangeFactory.CreateClient(exchangeName);

            // Test 1: Can read balance (basic authentication)
            try
            {
                var balances = await client.GetBalanceAsync();
                result.CanReadBalance = balances != null;
                result.Permissions["read_balance"] = true;
            }
            catch
            {
                result.CanReadBalance = false;
                result.Permissions["read_balance"] = false;
            }

            // Test 2: Can read open orders (trading permission check)
            try
            {
                var orders = await client.GetOpenOrdersAsync("BTCUSDT");
                result.CanTrade = true;
                result.Permissions["read_orders"] = true;
            }
            catch
            {
                // Some exchanges require actual trading permission to read orders
                result.Permissions["read_orders"] = false;
            }

            // Note: We don't test actual trading or withdrawals for safety

            result.IsValid = result.CanReadBalance;

            if (!result.IsValid)
            {
                result.ErrorMessage = "API key cannot read balance - check permissions";
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public void StartMonitoring(TimeSpan interval)
    {
        StopMonitoring();

        _monitoringTimer = new System.Timers.Timer(interval.TotalMilliseconds);
        _monitoringTimer.Elapsed += async (s, e) =>
        {
            try
            {
                await CheckAllConnectionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("ConnectionStatus", $"Monitoring error: {ex.Message}");
            }
        };
        _monitoringTimer.AutoReset = true;
        _monitoringTimer.Start();

        _logger.LogInfo("ConnectionStatus", $"Started connection monitoring (interval: {interval.TotalSeconds}s)");
    }

    public void StopMonitoring()
    {
        _monitoringTimer?.Stop();
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;

        _logger.LogInfo("ConnectionStatus", "Stopped connection monitoring");
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
