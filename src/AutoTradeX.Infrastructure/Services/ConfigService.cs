/*
 * ============================================================================
 * AutoTrade-X - Cross-Exchange Arbitrage Trading Bot
 * ============================================================================
 * ⚠️ Educational/Experimental Only - No profit guarantee
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// ConfigService - Implementation ของ IConfigService
///
/// หน้าที่หลัก:
/// 1. โหลด config จาก appsettings.json
/// 2. บันทึก config กลับไฟล์
/// 3. ดึง API Key จาก Environment Variables
/// 4. Validate config
///
/// ⚠️ สำคัญ:
/// - ห้ามเก็บ API Key/Secret ในไฟล์ config
/// - ใช้ Environment Variables เท่านั้น
/// </summary>
public class ConfigService : IConfigService
{
    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private AppConfig _currentConfig;
    private readonly object _lock = new();

    public event EventHandler<AppConfig>? ConfigChanged;

    public ConfigService(string configFilePath = "appsettings.json")
    {
        _configFilePath = configFilePath;

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        // โหลด config แบบ sync โดยไม่ block
        _currentConfig = LoadConfigSync();
    }

    /// <summary>
    /// โหลด config แบบ sync สำหรับ constructor
    /// </summary>
    private AppConfig LoadConfigSync()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                var defaultConfig = CreateDefaultConfig();
                var json = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
                File.WriteAllText(_configFilePath, json);
                return defaultConfig;
            }

            var configJson = File.ReadAllText(_configFilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(configJson, _jsonOptions);
            return config ?? CreateDefaultConfig();
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }

    #region Load/Save

    /// <summary>
    /// ดึง config ปัจจุบัน
    /// </summary>
    public AppConfig GetConfig()
    {
        lock (_lock)
        {
            return _currentConfig;
        }
    }

    /// <summary>
    /// โหลด config จากไฟล์
    /// </summary>
    public async Task<AppConfig> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                // สร้าง config default ถ้าไม่มีไฟล์
                var defaultConfig = CreateDefaultConfig();
                await SaveConfigAsync(defaultConfig);
                return defaultConfig;
            }

            var json = await File.ReadAllTextAsync(_configFilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);

            if (config == null)
            {
                throw new Exception("Failed to deserialize config");
            }

            lock (_lock)
            {
                _currentConfig = config;
            }

            return config;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config: {ex.Message}");
            return CreateDefaultConfig();
        }
    }

    /// <summary>
    /// บันทึก config ลงไฟล์
    /// </summary>
    public async Task SaveConfigAsync(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json);

            lock (_lock)
            {
                _currentConfig = config;
            }

            ConfigChanged?.Invoke(this, config);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to save config: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// อัพเดท config บางส่วน
    /// </summary>
    public async Task UpdateConfigAsync(Action<AppConfig> updateAction)
    {
        lock (_lock)
        {
            updateAction(_currentConfig);
        }

        await SaveConfigAsync(_currentConfig);
    }

    /// <summary>
    /// รีโหลด config จากไฟล์
    /// </summary>
    public async Task ReloadConfigAsync()
    {
        await LoadConfigAsync();
    }

    #endregion

    #region Validation

    /// <summary>
    /// ตรวจสอบ config ว่าถูกต้องหรือไม่
    /// </summary>
    public List<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();

        // ตรวจสอบ Exchange Config
        if (string.IsNullOrEmpty(config.ExchangeA.ApiBaseUrl))
            errors.Add("Exchange A: API Base URL is required");

        if (string.IsNullOrEmpty(config.ExchangeB.ApiBaseUrl))
            errors.Add("Exchange B: API Base URL is required");

        // ตรวจสอบ Strategy Config
        if (config.Strategy.MinSpreadPercentage < 0)
            errors.Add("Strategy: Min Spread Percentage cannot be negative");

        if (config.Strategy.MinExpectedProfitQuoteCurrency < 0)
            errors.Add("Strategy: Min Expected Profit cannot be negative");

        if (config.Strategy.PollingIntervalMs < 100)
            errors.Add("Strategy: Polling Interval must be at least 100ms");

        // ตรวจสอบ Risk Config
        if (config.Risk.MaxPositionSizePerTrade <= 0)
            errors.Add("Risk: Max Position Size must be positive");

        if (config.Risk.MaxDailyLoss <= 0)
            errors.Add("Risk: Max Daily Loss must be positive");

        if (config.Risk.MaxTradesPerDay <= 0)
            errors.Add("Risk: Max Trades Per Day must be positive");

        // ตรวจสอบ Trading Pairs
        if (config.TradingPairs == null || config.TradingPairs.Count == 0)
            errors.Add("At least one trading pair is required");

        foreach (var pair in config.TradingPairs ?? new List<string>())
        {
            if (!pair.Contains("/"))
            {
                errors.Add($"Invalid trading pair format: {pair}. Expected format: BASE/QUOTE");
            }
        }

        // ตรวจสอบ Live Trading credentials
        if (config.General.LiveTrading)
        {
            if (!HasValidCredentials(config.ExchangeA))
                errors.Add("Exchange A: API credentials required for live trading (set environment variables)");

            if (!HasValidCredentials(config.ExchangeB))
                errors.Add("Exchange B: API credentials required for live trading (set environment variables)");
        }

        return errors;
    }

    #endregion

    #region API Credentials

    /// <summary>
    /// ดึง API Key จาก Environment Variable
    /// ⚠️ ห้ามเก็บใน code หรือ config file
    /// </summary>
    public string? GetApiKey(string envVarName)
    {
        return Environment.GetEnvironmentVariable(envVarName);
    }

    /// <summary>
    /// ดึง API Secret จาก Environment Variable
    /// </summary>
    public string? GetApiSecret(string envVarName)
    {
        return Environment.GetEnvironmentVariable(envVarName);
    }

    /// <summary>
    /// ตรวจสอบว่า API Key/Secret ถูกตั้งค่าหรือยัง
    /// </summary>
    public bool HasValidCredentials(ExchangeConfig exchangeConfig)
    {
        var apiKey = GetApiKey(exchangeConfig.ApiKeyEnvVar);
        var apiSecret = GetApiSecret(exchangeConfig.ApiSecretEnvVar);

        return !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret);
    }

    #endregion

    #region Import/Export

    /// <summary>
    /// Export config เป็น JSON string
    /// </summary>
    public string ExportConfigJson()
    {
        return JsonSerializer.Serialize(_currentConfig, _jsonOptions);
    }

    /// <summary>
    /// Import config จาก JSON string
    /// </summary>
    public AppConfig ImportConfigJson(string json)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
        if (config == null)
        {
            throw new Exception("Failed to parse config JSON");
        }

        var errors = ValidateConfig(config);
        if (errors.Count > 0)
        {
            throw new Exception($"Config validation failed: {string.Join("; ", errors)}");
        }

        return config;
    }

    #endregion

    #region Default Config

    /// <summary>
    /// สร้าง Config default
    /// </summary>
    private AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            ExchangeA = new ExchangeConfig
            {
                Name = "ExchangeA",
                ApiBaseUrl = "https://api.exchange-a.com",
                ApiKeyEnvVar = "EXCHANGE_A_API_KEY",
                ApiSecretEnvVar = "EXCHANGE_A_API_SECRET",
                TradingFeePercent = 0.1m,
                RateLimitPerSecond = 10,
                TimeoutMs = 10000,
                MaxRetries = 3,
                IsEnabled = true
            },
            ExchangeB = new ExchangeConfig
            {
                Name = "ExchangeB",
                ApiBaseUrl = "https://api.exchange-b.com",
                ApiKeyEnvVar = "EXCHANGE_B_API_KEY",
                ApiSecretEnvVar = "EXCHANGE_B_API_SECRET",
                TradingFeePercent = 0.1m,
                RateLimitPerSecond = 10,
                TimeoutMs = 10000,
                MaxRetries = 3,
                IsEnabled = true
            },
            Strategy = new StrategyConfig
            {
                MinSpreadPercentage = 0.3m,
                MinExpectedProfitQuoteCurrency = 0.5m,
                PollingIntervalMs = 1000,
                UseWebSocket = false,
                OrderType = "Market",
                LimitOrderSlippagePercent = 0.05m,
                OrderFillTimeoutMs = 5000,
                PartialFillStrategy = "CancelRemaining",
                OneSideFailStrategy = "Hedge",
                OrderBookDepth = 20,
                MinDepthQuantity = 0.1m
            },
            Risk = new RiskConfig
            {
                MaxPositionSizePerTrade = 100m,
                MaxDailyLoss = 50m,
                MaxTradesPerDay = 100,
                MaxTradesPerHour = 20,
                MinTimeBetweenTradesMs = 5000,
                MaxConsecutiveLosses = 5,
                MaxBalancePercentPerTrade = 10m,
                MinBalanceRequired = 50m
            },
            General = new GeneralConfig
            {
                LiveTrading = false, // ⚠️ เริ่มต้นด้วย Simulation mode
                LogDirectory = "logs",
                LogLevel = "Info",
                LogRetentionDays = 30,
                DarkTheme = true,
                EnableNotifications = true,
                PlaySoundOnTrade = false,
                Version = "1.0.0"
            },
            TradingPairs = new List<string>
            {
                "BTC/USDT",
                "ETH/USDT"
            }
        };
    }

    #endregion
}
