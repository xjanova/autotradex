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
    private readonly IApiCredentialsService? _apiCredentialsService;

    private ConnectionStatusSnapshot _currentStatus = new();
    private System.Timers.Timer? _monitoringTimer;
    private readonly object _statusLock = new();

    // Cache ของ verified exchanges (จำสถานะที่ test ผ่านแล้ว)
    // Use OrdinalIgnoreCase so "Binance", "binance", "BINANCE" all match
    private readonly Dictionary<string, ExchangeConnectionStatus> _verifiedExchanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _verifiedLock = new();

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
        ILoggingService logger,
        IApiCredentialsService? apiCredentialsService = null)
    {
        _exchangeFactory = exchangeFactory;
        _configService = configService;
        _strategyService = strategyService;
        _logger = logger;
        _apiCredentialsService = apiCredentialsService;
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
        _logger.LogInfo("ConnectionStatus", $"CheckExchangeConnectionAsync: Checking '{exchangeName}'");

        var status = new ExchangeConnectionStatus
        {
            ExchangeName = exchangeName,
            LastChecked = DateTime.UtcNow
        };

        try
        {
            // Check credentials directly from environment variables (not tied to ExchangeA/B config)
            // ตรวจสอบ credentials โดยตรงจาก environment variables (ไม่ผูกกับ ExchangeA/B config)
            var (apiKeyEnv, apiSecretEnv) = GetExchangeEnvVarNames(exchangeName);
            var apiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
            var apiSecret = Environment.GetEnvironmentVariable(apiSecretEnv);

            _logger.LogInfo("ConnectionStatus", $"CheckExchangeConnectionAsync: '{exchangeName}' - EnvVars: {apiKeyEnv}={!string.IsNullOrEmpty(apiKey)}, {apiSecretEnv}={!string.IsNullOrEmpty(apiSecret)}");

            // ถ้าไม่พบใน environment variables ให้ลองอ่านจาก database
            // If not found in environment variables, try loading from database
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                var dbCredentials = await TryLoadCredentialsFromDatabaseAsync(exchangeName);
                if (dbCredentials != null)
                {
                    apiKey = dbCredentials.Value.apiKey;
                    apiSecret = dbCredentials.Value.apiSecret;
                    _logger.LogInfo("ConnectionStatus", $"CheckExchangeConnectionAsync: '{exchangeName}' - Loaded from database");
                }
            }

            var hasCredentials = !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret);
            status.HasValidCredentials = hasCredentials;

            if (!hasCredentials)
            {
                _logger.LogInfo("ConnectionStatus", $"CheckExchangeConnectionAsync: '{exchangeName}' - No credentials, returning early");
                status.IsConnected = false;
                status.ErrorMessage = "API key not configured";
                return status;
            }

            // ตรวจสอบ cached verified status ก่อน - ถ้า test ผ่านแล้วไม่ต้อง test ใหม่
            // Check cached verified status first - skip re-testing if already verified
            var verifiedStatus = GetVerifiedStatus(exchangeName);
            if (verifiedStatus != null)
            {
                _logger.LogInfo("ConnectionStatus", $"{exchangeName}: Using cached verified status");
                verifiedStatus.LastChecked = DateTime.UtcNow;
                return verifiedStatus;
            }

            // Test actual connection with authenticated endpoint (use real client, not simulation)
            // ทดสอบการเชื่อมต่อจริง (ใช้ real client ไม่ใช่ simulation)
            using var client = _exchangeFactory.CreateRealClient(exchangeName);

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

                // Auto-cache verified status for successful connections
                // บันทึก verified status อัตโนมัติเมื่อ test ผ่าน
                MarkExchangeAsVerified(exchangeName, status.CanTrade);
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
            using var client = _exchangeFactory.CreateClient(exchangeName);

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

    /// <summary>
    /// Mark exchange as verified (called after successful test in Settings)
    /// บันทึกว่า exchange นี้ test ผ่านแล้ว
    /// </summary>
    public void MarkExchangeAsVerified(string exchangeName, bool canTrade = true)
    {
        _logger.LogInfo("ConnectionStatus", $"MarkExchangeAsVerified: Adding '{exchangeName}' to verified cache");

        lock (_verifiedLock)
        {
            var status = new ExchangeConnectionStatus
            {
                ExchangeName = exchangeName,
                IsConnected = true,
                HasValidCredentials = true,
                CanReadBalance = true,
                CanTrade = canTrade,
                LastChecked = DateTime.UtcNow,
                Latency = 0
            };

            _verifiedExchanges[exchangeName] = status;
            _logger.LogInfo("ConnectionStatus", $"MarkExchangeAsVerified: '{exchangeName}' added. Cache now has {_verifiedExchanges.Count} entries: [{string.Join(", ", _verifiedExchanges.Keys)}]");
        }

        // Also update current status snapshot
        ConnectionStatusSnapshot snapshotToSend;
        lock (_statusLock)
        {
            _currentStatus.Exchanges[exchangeName] = new ExchangeConnectionStatus
            {
                ExchangeName = exchangeName,
                IsConnected = true,
                HasValidCredentials = true,
                CanReadBalance = true,
                CanTrade = canTrade,
                LastChecked = DateTime.UtcNow
            };

            // Create a copy of current status to send with event
            // สร้าง copy ของ status ปัจจุบันเพื่อส่งกับ event
            snapshotToSend = new ConnectionStatusSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Exchanges = new Dictionary<string, ExchangeConnectionStatus>(_currentStatus.Exchanges),
                HasActiveStrategy = _currentStatus.HasActiveStrategy,
                ActiveStrategyName = _currentStatus.ActiveStrategyName,
                HasTradingPairs = _currentStatus.HasTradingPairs,
                TradingPairCount = _currentStatus.TradingPairCount,
                IsLiveMode = _currentStatus.IsLiveMode
            };
        }

        // Raise event with updated snapshot
        _logger.LogInfo("ConnectionStatus", $"Firing ConnectionStatusChanged event for {exchangeName}");
        ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs(snapshotToSend, exchangeName, $"{exchangeName} verified"));
    }

    /// <summary>
    /// Check if exchange has been verified in this session
    /// </summary>
    public bool IsExchangeVerified(string exchangeName)
    {
        lock (_verifiedLock)
        {
            return _verifiedExchanges.ContainsKey(exchangeName);
        }
    }

    /// <summary>
    /// Get cached verified status for exchange
    /// </summary>
    public ExchangeConnectionStatus? GetVerifiedStatus(string exchangeName)
    {
        lock (_verifiedLock)
        {
            _logger.LogInfo("ConnectionStatus", $"GetVerifiedStatus: Looking for '{exchangeName}', cache has {_verifiedExchanges.Count} entries: [{string.Join(", ", _verifiedExchanges.Keys)}]");

            if (_verifiedExchanges.TryGetValue(exchangeName, out var status))
            {
                _logger.LogInfo("ConnectionStatus", $"GetVerifiedStatus: FOUND '{exchangeName}' in cache!");
                return status;
            }
            _logger.LogInfo("ConnectionStatus", $"GetVerifiedStatus: '{exchangeName}' NOT found in cache");
            return null;
        }
    }

    /// <summary>
    /// Clear verified status for exchange (when API key changes)
    /// </summary>
    public void ClearVerifiedStatus(string exchangeName)
    {
        lock (_verifiedLock)
        {
            _verifiedExchanges.Remove(exchangeName);
            _logger.LogInfo("ConnectionStatus", $"{exchangeName}: Verified status cleared");
        }
    }

    /// <summary>
    /// Get environment variable names for each exchange's API credentials
    /// ดึงชื่อ environment variables สำหรับ API credentials ของแต่ละ exchange
    /// </summary>
    private (string apiKeyEnv, string apiSecretEnv) GetExchangeEnvVarNames(string exchangeName)
    {
        return exchangeName.ToLower() switch
        {
            "binance" => ("AUTOTRADEX_BINANCE_API_KEY", "AUTOTRADEX_BINANCE_API_SECRET"),
            "kucoin" => ("AUTOTRADEX_KUCOIN_API_KEY", "AUTOTRADEX_KUCOIN_API_SECRET"),
            "okx" => ("AUTOTRADEX_OKX_API_KEY", "AUTOTRADEX_OKX_API_SECRET"),
            "bybit" => ("AUTOTRADEX_BYBIT_API_KEY", "AUTOTRADEX_BYBIT_API_SECRET"),
            "gate.io" => ("AUTOTRADEX_GATEIO_API_KEY", "AUTOTRADEX_GATEIO_API_SECRET"),
            "bitkub" => ("AUTOTRADEX_BITKUB_API_KEY", "AUTOTRADEX_BITKUB_API_SECRET"),
            _ => ($"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "")}_API_KEY",
                  $"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "")}_API_SECRET")
        };
    }

    /// <summary>
    /// Try to load credentials from database if ApiCredentialsService is available
    /// ลองโหลด credentials จาก database ถ้า ApiCredentialsService พร้อมใช้งาน
    /// </summary>
    private async Task<(string apiKey, string apiSecret)?> TryLoadCredentialsFromDatabaseAsync(string exchangeName)
    {
        if (_apiCredentialsService == null)
        {
            _logger.LogInfo("ConnectionStatus", $"TryLoadCredentialsFromDatabase: ApiCredentialsService is null");
            return null;
        }

        try
        {
            var credentials = await _apiCredentialsService.GetCredentialsAsync(exchangeName);
            if (credentials != null && !string.IsNullOrEmpty(credentials.ApiKey) && !string.IsNullOrEmpty(credentials.ApiSecret))
            {
                // Set environment variables for this exchange so other parts of the app can use them
                // ตั้งค่า environment variables ให้ส่วนอื่นของ app ใช้งานได้
                var (keyEnv, secretEnv) = GetExchangeEnvVarNames(exchangeName);
                Environment.SetEnvironmentVariable(keyEnv, credentials.ApiKey);
                Environment.SetEnvironmentVariable(secretEnv, credentials.ApiSecret);

                // Also set passphrase if available
                if (!string.IsNullOrEmpty(credentials.Passphrase))
                {
                    var passEnv = exchangeName.ToLower() == "okx"
                        ? "AUTOTRADEX_OKX_PASSPHRASE"
                        : $"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "")}_API_KEY_PASSPHRASE";
                    Environment.SetEnvironmentVariable(passEnv, credentials.Passphrase);
                }

                _logger.LogInfo("ConnectionStatus", $"TryLoadCredentialsFromDatabase: Loaded and set env vars for {exchangeName}");
                return (credentials.ApiKey, credentials.ApiSecret);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("ConnectionStatus", $"TryLoadCredentialsFromDatabase: Error for {exchangeName}: {ex.Message}");
        }

        return null;
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
