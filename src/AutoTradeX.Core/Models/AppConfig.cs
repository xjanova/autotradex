// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public class AppConfig
{
    public ExchangeConfig ExchangeA { get; set; } = new();
    public ExchangeConfig ExchangeB { get; set; } = new();
    public StrategyConfig Strategy { get; set; } = new();
    public RiskConfig Risk { get; set; } = new();
    public GeneralConfig General { get; set; } = new();
    public List<string> TradingPairs { get; set; } = new() { "BTC/USDT", "ETH/USDT" };
}

public class ExchangeConfig
{
    public string Name { get; set; } = "ExchangeA";
    public string ApiBaseUrl { get; set; } = "https://api.exchange.com";
    public string? WebSocketUrl { get; set; }
    public string ApiKeyEnvVar { get; set; } = "EXCHANGE_A_API_KEY";
    public string ApiSecretEnvVar { get; set; } = "EXCHANGE_A_API_SECRET";
    public string? PassphraseEnvVar { get; set; }
    public decimal TradingFeePercent { get; set; } = 0.1m;
    public int RateLimitPerSecond { get; set; } = 10;
    public int TimeoutMs { get; set; } = 10000;
    public int MaxRetries { get; set; } = 3;
    public bool IsEnabled { get; set; } = true;
}

public class StrategyConfig
{
    public decimal MinSpreadPercentage { get; set; } = 0.3m;
    public decimal MinExpectedProfitQuoteCurrency { get; set; } = 0.5m;
    public int PollingIntervalMs { get; set; } = 1000;
    public bool UseWebSocket { get; set; } = false;
    public string OrderType { get; set; } = "Market";
    public decimal LimitOrderSlippagePercent { get; set; } = 0.05m;
    public int OrderFillTimeoutMs { get; set; } = 5000;
    public string PartialFillStrategy { get; set; } = "CancelRemaining";
    public string OneSideFailStrategy { get; set; } = "Hedge";
    public int OrderBookDepth { get; set; } = 20;
    public decimal MinDepthQuantity { get; set; } = 0.1m;
}

public class RiskConfig
{
    public decimal MaxPositionSizePerTrade { get; set; } = 100m;
    public decimal MaxDailyLoss { get; set; } = 50m;
    public int MaxTradesPerDay { get; set; } = 100;
    public int MaxTradesPerHour { get; set; } = 20;
    public int MinTimeBetweenTradesMs { get; set; } = 5000;
    public int MaxConsecutiveLosses { get; set; } = 5;
    public decimal MaxBalancePercentPerTrade { get; set; } = 10m;
    public decimal MinBalanceRequired { get; set; } = 50m;

    // Emergency protection settings
    public decimal MaxDrawdownPercent { get; set; } = 5m;
    public decimal EmergencyStopLossPercent { get; set; } = 10m;
    public bool EnableEmergencyProtection { get; set; } = true;
    public decimal RebalanceThresholdPercent { get; set; } = 30m;
    public int RapidLossWindowTrades { get; set; } = 5;
    public decimal RapidLossThresholdPercent { get; set; } = 1m;
}

public class GeneralConfig
{
    public bool LiveTrading { get; set; } = false;
    public string LogDirectory { get; set; } = "logs";
    public string LogLevel { get; set; } = "Info";
    public int LogRetentionDays { get; set; } = 30;
    public bool DarkTheme { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool PlaySoundOnTrade { get; set; } = false;
    public string Version { get; set; } = "1.0.0";
}
