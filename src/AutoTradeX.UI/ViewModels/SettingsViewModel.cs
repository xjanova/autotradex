// AutoTrade-X v1.0.0

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.ExchangeClients;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Input;
using Microsoft.Win32;

namespace AutoTradeX.UI.ViewModels;

/// <summary>
/// SettingsViewModel - ViewModel for Settings page
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    // ========== Exchange A Config ==========

    private string _exchangeA_Name = string.Empty;
    public string ExchangeA_Name
    {
        get => _exchangeA_Name;
        set => SetProperty(ref _exchangeA_Name, value);
    }

    private string _exchangeA_ApiBaseUrl = string.Empty;
    public string ExchangeA_ApiBaseUrl
    {
        get => _exchangeA_ApiBaseUrl;
        set => SetProperty(ref _exchangeA_ApiBaseUrl, value);
    }

    private decimal _exchangeA_FeePercent;
    public decimal ExchangeA_FeePercent
    {
        get => _exchangeA_FeePercent;
        set => SetProperty(ref _exchangeA_FeePercent, value);
    }

    private bool _exchangeA_IsEnabled;
    public bool ExchangeA_IsEnabled
    {
        get => _exchangeA_IsEnabled;
        set => SetProperty(ref _exchangeA_IsEnabled, value);
    }

    private bool _exchangeA_HasCredentials;
    public bool ExchangeA_HasCredentials
    {
        get => _exchangeA_HasCredentials;
        set => SetProperty(ref _exchangeA_HasCredentials, value);
    }

    // ========== Exchange B Config ==========

    private string _exchangeB_Name = string.Empty;
    public string ExchangeB_Name
    {
        get => _exchangeB_Name;
        set => SetProperty(ref _exchangeB_Name, value);
    }

    private string _exchangeB_ApiBaseUrl = string.Empty;
    public string ExchangeB_ApiBaseUrl
    {
        get => _exchangeB_ApiBaseUrl;
        set => SetProperty(ref _exchangeB_ApiBaseUrl, value);
    }

    private decimal _exchangeB_FeePercent;
    public decimal ExchangeB_FeePercent
    {
        get => _exchangeB_FeePercent;
        set => SetProperty(ref _exchangeB_FeePercent, value);
    }

    private bool _exchangeB_IsEnabled;
    public bool ExchangeB_IsEnabled
    {
        get => _exchangeB_IsEnabled;
        set => SetProperty(ref _exchangeB_IsEnabled, value);
    }

    private bool _exchangeB_HasCredentials;
    public bool ExchangeB_HasCredentials
    {
        get => _exchangeB_HasCredentials;
        set => SetProperty(ref _exchangeB_HasCredentials, value);
    }

    // ========== Strategy Config ==========

    private decimal _minSpreadPercent;
    public decimal MinSpreadPercent
    {
        get => _minSpreadPercent;
        set => SetProperty(ref _minSpreadPercent, value);
    }

    private decimal _minExpectedProfit;
    public decimal MinExpectedProfit
    {
        get => _minExpectedProfit;
        set => SetProperty(ref _minExpectedProfit, value);
    }

    private int _pollingIntervalMs;
    public int PollingIntervalMs
    {
        get => _pollingIntervalMs;
        set => SetProperty(ref _pollingIntervalMs, value);
    }

    private bool _useWebSocket;
    public bool UseWebSocket
    {
        get => _useWebSocket;
        set => SetProperty(ref _useWebSocket, value);
    }

    private string _orderType = "Market";
    public string OrderType
    {
        get => _orderType;
        set => SetProperty(ref _orderType, value);
    }

    private decimal _limitOrderSlippage;
    public decimal LimitOrderSlippage
    {
        get => _limitOrderSlippage;
        set => SetProperty(ref _limitOrderSlippage, value);
    }

    private int _orderFillTimeoutMs;
    public int OrderFillTimeoutMs
    {
        get => _orderFillTimeoutMs;
        set => SetProperty(ref _orderFillTimeoutMs, value);
    }

    private string _partialFillStrategy = "CancelRemaining";
    public string PartialFillStrategy
    {
        get => _partialFillStrategy;
        set => SetProperty(ref _partialFillStrategy, value);
    }

    private string _oneSideFailStrategy = "Hedge";
    public string OneSideFailStrategy
    {
        get => _oneSideFailStrategy;
        set => SetProperty(ref _oneSideFailStrategy, value);
    }

    // ========== Risk Config ==========

    private decimal _maxPositionSize;
    public decimal MaxPositionSize
    {
        get => _maxPositionSize;
        set => SetProperty(ref _maxPositionSize, value);
    }

    private decimal _maxDailyLoss;
    public decimal MaxDailyLoss
    {
        get => _maxDailyLoss;
        set => SetProperty(ref _maxDailyLoss, value);
    }

    private int _maxTradesPerDay;
    public int MaxTradesPerDay
    {
        get => _maxTradesPerDay;
        set => SetProperty(ref _maxTradesPerDay, value);
    }

    private int _maxTradesPerHour;
    public int MaxTradesPerHour
    {
        get => _maxTradesPerHour;
        set => SetProperty(ref _maxTradesPerHour, value);
    }

    private int _minTimeBetweenTradesMs;
    public int MinTimeBetweenTradesMs
    {
        get => _minTimeBetweenTradesMs;
        set => SetProperty(ref _minTimeBetweenTradesMs, value);
    }

    private int _maxConsecutiveLosses;
    public int MaxConsecutiveLosses
    {
        get => _maxConsecutiveLosses;
        set => SetProperty(ref _maxConsecutiveLosses, value);
    }

    private decimal _maxBalancePercentPerTrade;
    public decimal MaxBalancePercentPerTrade
    {
        get => _maxBalancePercentPerTrade;
        set => SetProperty(ref _maxBalancePercentPerTrade, value);
    }

    private decimal _minBalanceRequired;
    public decimal MinBalanceRequired
    {
        get => _minBalanceRequired;
        set => SetProperty(ref _minBalanceRequired, value);
    }

    // ========== General Config ==========

    private bool _liveTrading;
    public bool LiveTrading
    {
        get => _liveTrading;
        set => SetProperty(ref _liveTrading, value);
    }

    private string _logLevel = "Info";
    public string LogLevel
    {
        get => _logLevel;
        set => SetProperty(ref _logLevel, value);
    }

    private int _logRetentionDays;
    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set => SetProperty(ref _logRetentionDays, value);
    }

    private bool _darkTheme;
    public bool DarkTheme
    {
        get => _darkTheme;
        set => SetProperty(ref _darkTheme, value);
    }

    private bool _enableNotifications;
    public bool EnableNotifications
    {
        get => _enableNotifications;
        set => SetProperty(ref _enableNotifications, value);
    }

    private bool _playSoundOnTrade;
    public bool PlaySoundOnTrade
    {
        get => _playSoundOnTrade;
        set => SetProperty(ref _playSoundOnTrade, value);
    }

    // ========== Trading Pairs ==========

    private string _tradingPairsText = string.Empty;
    public string TradingPairsText
    {
        get => _tradingPairsText;
        set => SetProperty(ref _tradingPairsText, value);
    }

    // ========== Validation ==========

    private string _validationMessage = string.Empty;
    public string ValidationMessage
    {
        get => _validationMessage;
        set => SetProperty(ref _validationMessage, value);
    }

    private bool _hasValidationErrors;
    public bool HasValidationErrors
    {
        get => _hasValidationErrors;
        set => SetProperty(ref _hasValidationErrors, value);
    }

    // ========== Commands ==========

    public ICommand SaveConfigCommand { get; }
    public ICommand ResetToDefaultsCommand { get; }
    public ICommand TestExchangeAConnectionCommand { get; }
    public ICommand TestExchangeBConnectionCommand { get; }
    public ICommand ExportConfigCommand { get; }
    public ICommand ImportConfigCommand { get; }

    // ========== Constructor ==========

    public SettingsViewModel()
    {
        _configService = App.Services.GetRequiredService<IConfigService>();
        _logger = App.Services.GetRequiredService<ILoggingService>();

        // Initialize commands
        SaveConfigCommand = new AsyncRelayCommand(SaveConfigAsync);
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);
        TestExchangeAConnectionCommand = new AsyncRelayCommand(TestExchangeAConnectionAsync);
        TestExchangeBConnectionCommand = new AsyncRelayCommand(TestExchangeBConnectionAsync);
        ExportConfigCommand = new RelayCommand(ExportConfig);
        ImportConfigCommand = new RelayCommand(ImportConfig);

        LoadConfig();
    }

    // ========== Methods ==========

    private void LoadConfig()
    {
        var config = _configService.GetConfig();

        // Exchange A
        ExchangeA_Name = config.ExchangeA.Name;
        ExchangeA_ApiBaseUrl = config.ExchangeA.ApiBaseUrl;
        ExchangeA_FeePercent = config.ExchangeA.TradingFeePercent;
        ExchangeA_IsEnabled = config.ExchangeA.IsEnabled;
        ExchangeA_HasCredentials = _configService.HasValidCredentials(config.ExchangeA);

        // Exchange B
        ExchangeB_Name = config.ExchangeB.Name;
        ExchangeB_ApiBaseUrl = config.ExchangeB.ApiBaseUrl;
        ExchangeB_FeePercent = config.ExchangeB.TradingFeePercent;
        ExchangeB_IsEnabled = config.ExchangeB.IsEnabled;
        ExchangeB_HasCredentials = _configService.HasValidCredentials(config.ExchangeB);

        // Strategy
        MinSpreadPercent = config.Strategy.MinSpreadPercentage;
        MinExpectedProfit = config.Strategy.MinExpectedProfitQuoteCurrency;
        PollingIntervalMs = config.Strategy.PollingIntervalMs;
        UseWebSocket = config.Strategy.UseWebSocket;
        OrderType = config.Strategy.OrderType;
        LimitOrderSlippage = config.Strategy.LimitOrderSlippagePercent;
        OrderFillTimeoutMs = config.Strategy.OrderFillTimeoutMs;
        PartialFillStrategy = config.Strategy.PartialFillStrategy;
        OneSideFailStrategy = config.Strategy.OneSideFailStrategy;

        // Risk
        MaxPositionSize = config.Risk.MaxPositionSizePerTrade;
        MaxDailyLoss = config.Risk.MaxDailyLoss;
        MaxTradesPerDay = config.Risk.MaxTradesPerDay;
        MaxTradesPerHour = config.Risk.MaxTradesPerHour;
        MinTimeBetweenTradesMs = config.Risk.MinTimeBetweenTradesMs;
        MaxConsecutiveLosses = config.Risk.MaxConsecutiveLosses;
        MaxBalancePercentPerTrade = config.Risk.MaxBalancePercentPerTrade;
        MinBalanceRequired = config.Risk.MinBalanceRequired;

        // General
        LiveTrading = config.General.LiveTrading;
        LogLevel = config.General.LogLevel;
        LogRetentionDays = config.General.LogRetentionDays;
        DarkTheme = config.General.DarkTheme;
        EnableNotifications = config.General.EnableNotifications;
        PlaySoundOnTrade = config.General.PlaySoundOnTrade;

        // Trading Pairs
        TradingPairsText = string.Join("\n", config.TradingPairs);
    }

    private AppConfig BuildConfigFromViewModel()
    {
        var config = _configService.GetConfig();

        // Exchange A
        config.ExchangeA.Name = ExchangeA_Name;
        config.ExchangeA.ApiBaseUrl = ExchangeA_ApiBaseUrl;
        config.ExchangeA.TradingFeePercent = ExchangeA_FeePercent;
        config.ExchangeA.IsEnabled = ExchangeA_IsEnabled;

        // Exchange B
        config.ExchangeB.Name = ExchangeB_Name;
        config.ExchangeB.ApiBaseUrl = ExchangeB_ApiBaseUrl;
        config.ExchangeB.TradingFeePercent = ExchangeB_FeePercent;
        config.ExchangeB.IsEnabled = ExchangeB_IsEnabled;

        // Strategy
        config.Strategy.MinSpreadPercentage = MinSpreadPercent;
        config.Strategy.MinExpectedProfitQuoteCurrency = MinExpectedProfit;
        config.Strategy.PollingIntervalMs = PollingIntervalMs;
        config.Strategy.UseWebSocket = UseWebSocket;
        config.Strategy.OrderType = OrderType;
        config.Strategy.LimitOrderSlippagePercent = LimitOrderSlippage;
        config.Strategy.OrderFillTimeoutMs = OrderFillTimeoutMs;
        config.Strategy.PartialFillStrategy = PartialFillStrategy;
        config.Strategy.OneSideFailStrategy = OneSideFailStrategy;

        // Risk
        config.Risk.MaxPositionSizePerTrade = MaxPositionSize;
        config.Risk.MaxDailyLoss = MaxDailyLoss;
        config.Risk.MaxTradesPerDay = MaxTradesPerDay;
        config.Risk.MaxTradesPerHour = MaxTradesPerHour;
        config.Risk.MinTimeBetweenTradesMs = MinTimeBetweenTradesMs;
        config.Risk.MaxConsecutiveLosses = MaxConsecutiveLosses;
        config.Risk.MaxBalancePercentPerTrade = MaxBalancePercentPerTrade;
        config.Risk.MinBalanceRequired = MinBalanceRequired;

        // General
        config.General.LiveTrading = LiveTrading;
        config.General.LogLevel = LogLevel;
        config.General.LogRetentionDays = LogRetentionDays;
        config.General.DarkTheme = DarkTheme;
        config.General.EnableNotifications = EnableNotifications;
        config.General.PlaySoundOnTrade = PlaySoundOnTrade;

        // Trading Pairs
        config.TradingPairs = TradingPairsText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        return config;
    }

    private bool ValidateConfig()
    {
        var config = BuildConfigFromViewModel();
        var errors = _configService.ValidateConfig(config);

        HasValidationErrors = errors.Count > 0;
        ValidationMessage = errors.Count > 0
            ? string.Join("\n", errors)
            : string.Empty;

        return !HasValidationErrors;
    }

    // ========== Command Implementations ==========

    private async Task SaveConfigAsync()
    {
        await ExecuteAsync(async () =>
        {
            if (!ValidateConfig())
            {
                ShowError($"Invalid configuration:\n{ValidationMessage}");
                return;
            }

            // Show warning if Live Trading is enabled
            if (LiveTrading)
            {
                var confirm = Confirm(
                    "WARNING!\n\n" +
                    "You are about to enable Live Trading mode.\n" +
                    "The bot will trade with real money!\n\n" +
                    "Crypto trading is high risk.\n" +
                    "No profit is guaranteed.\n\n" +
                    "Do you want to continue?",
                    "Live Trading Warning"
                );

                if (!confirm)
                {
                    LiveTrading = false;
                    return;
                }
            }

            var config = BuildConfigFromViewModel();
            await _configService.SaveConfigAsync(config);

            _logger.LogInfo("Settings", "Configuration saved");
            ShowInfo("Configuration saved successfully");

        }, "Saving...");
    }

    private void ResetToDefaults()
    {
        if (Confirm("Reset all settings to default values?"))
        {
            var defaultConfig = new AppConfig();

            // Strategy
            MinSpreadPercent = defaultConfig.Strategy.MinSpreadPercentage;
            MinExpectedProfit = defaultConfig.Strategy.MinExpectedProfitQuoteCurrency;
            PollingIntervalMs = defaultConfig.Strategy.PollingIntervalMs;
            OrderType = defaultConfig.Strategy.OrderType;

            // Risk
            MaxPositionSize = defaultConfig.Risk.MaxPositionSizePerTrade;
            MaxDailyLoss = defaultConfig.Risk.MaxDailyLoss;
            MaxTradesPerDay = defaultConfig.Risk.MaxTradesPerDay;

            // General - always start with simulation
            LiveTrading = false;

            _logger.LogInfo("Settings", "Settings reset to defaults");
        }
    }

    private async Task TestExchangeAConnectionAsync()
    {
        await ExecuteAsync(async () =>
        {
            var config = new ExchangeConfig
            {
                Name = ExchangeA_Name,
                ApiBaseUrl = GetApiBaseUrl(ExchangeA_Name, ExchangeA_ApiBaseUrl),
                ApiKeyEnvVar = _configService.GetConfig().ExchangeA.ApiKeyEnvVar,
                ApiSecretEnvVar = _configService.GetConfig().ExchangeA.ApiSecretEnvVar,
                TradingFeePercent = ExchangeA_FeePercent,
                TimeoutMs = 10000,
                RateLimitPerSecond = 10,
                MaxRetries = 2
            };

            var client = CreateExchangeClient(ExchangeA_Name, config);
            if (client == null)
            {
                ShowError($"Unknown exchange type: {ExchangeA_Name}");
                return;
            }

            using (client)
            {
                var success = await client.TestConnectionAsync();
                if (success)
                {
                    ExchangeA_HasCredentials = true;
                    _logger.LogInfo("Settings", $"Exchange A ({ExchangeA_Name}) connection test successful");
                    ShowInfo($"Connection to {ExchangeA_Name} successful!");
                }
                else
                {
                    ExchangeA_HasCredentials = false;
                    ShowError($"Failed to connect to {ExchangeA_Name}. Please check your API credentials.");
                }
            }
        }, $"Testing {ExchangeA_Name} connection...");
    }

    private async Task TestExchangeBConnectionAsync()
    {
        await ExecuteAsync(async () =>
        {
            var config = new ExchangeConfig
            {
                Name = ExchangeB_Name,
                ApiBaseUrl = GetApiBaseUrl(ExchangeB_Name, ExchangeB_ApiBaseUrl),
                ApiKeyEnvVar = _configService.GetConfig().ExchangeB.ApiKeyEnvVar,
                ApiSecretEnvVar = _configService.GetConfig().ExchangeB.ApiSecretEnvVar,
                TradingFeePercent = ExchangeB_FeePercent,
                TimeoutMs = 10000,
                RateLimitPerSecond = 10,
                MaxRetries = 2
            };

            var client = CreateExchangeClient(ExchangeB_Name, config);
            if (client == null)
            {
                ShowError($"Unknown exchange type: {ExchangeB_Name}");
                return;
            }

            using (client)
            {
                var success = await client.TestConnectionAsync();
                if (success)
                {
                    ExchangeB_HasCredentials = true;
                    _logger.LogInfo("Settings", $"Exchange B ({ExchangeB_Name}) connection test successful");
                    ShowInfo($"Connection to {ExchangeB_Name} successful!");
                }
                else
                {
                    ExchangeB_HasCredentials = false;
                    ShowError($"Failed to connect to {ExchangeB_Name}. Please check your API credentials.");
                }
            }
        }, $"Testing {ExchangeB_Name} connection...");
    }

    private IExchangeClient? CreateExchangeClient(string name, ExchangeConfig config)
    {
        var lowerName = name.ToLowerInvariant();

        if (lowerName.Contains("binance"))
        {
            config.ApiBaseUrl = "https://api.binance.com";
            return new BinanceClient(config, _logger);
        }

        if (lowerName.Contains("bybit"))
        {
            config.ApiBaseUrl = "https://api.bybit.com";
            return new BybitClient(config, _logger);
        }

        // For simulation/unknown, use simulation client
        if (lowerName.Contains("sim"))
        {
            return new SimulationExchangeClient(name, _logger, true);
        }

        return null;
    }

    private string GetApiBaseUrl(string exchangeName, string configuredUrl)
    {
        if (!string.IsNullOrEmpty(configuredUrl) && !configuredUrl.Contains("placeholder"))
            return configuredUrl;

        var lowerName = exchangeName.ToLowerInvariant();

        if (lowerName.Contains("binance"))
            return "https://api.binance.com";

        if (lowerName.Contains("bybit"))
            return "https://api.bybit.com";

        return configuredUrl;
    }

    private void ExportConfig()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"autotradex-config-{DateTime.Now:yyyyMMdd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                var json = _configService.ExportConfigJson();
                System.IO.File.WriteAllText(dialog.FileName, json);
                _logger.LogInfo("Settings", $"Configuration exported to {dialog.FileName}");
                ShowInfo($"Configuration exported to:\n{dialog.FileName}");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Cannot export config: {ex.Message}");
        }
    }

    private void ImportConfig()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (dialog.ShowDialog() == true)
            {
                var json = System.IO.File.ReadAllText(dialog.FileName);
                _configService.ImportConfigJson(json);
                LoadConfig(); // Reload UI from imported config
                _logger.LogInfo("Settings", $"Configuration imported from {dialog.FileName}");
                ShowInfo("Configuration imported successfully.\nPlease review and save.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Cannot import config: {ex.Message}");
        }
    }
}
